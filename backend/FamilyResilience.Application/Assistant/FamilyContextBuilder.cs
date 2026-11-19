using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Builds the <see cref="FamilyContext"/> for the signed-in caller from the platform's own records, through the other
/// modules' use cases and read interfaces, with the same access rules as the participant API: only the members the
/// caller may act for (D-49), and score values only when the caller is the household's head (D-103). Nothing comes
/// from the request except the language. Reads are not audited here; the use case that sends the context to a model
/// audits that.
/// </summary>
public sealed class FamilyContextBuilder(
    ICallerAccess access,
    IParticipantAccess participant,
    ListMyConsents consents,
    ListMyAssignments assignments,
    ListMyRecommendations recommendations,
    IAssessmentSessionRepository sessions,
    IInstrumentVersionRepository versions,
    IScoreResultRepository scoreResults,
    TimeProvider timeProvider)
{
    /// <summary>The caller's family context in <paramref name="language"/>, or null when the caller has no household yet.</summary>
    public async Task<FamilyContext?> BuildAsync(string language, CancellationToken cancellationToken)
    {
        if (!AssistantLanguages.IsSupported(language))
        {
            throw new ArgumentException("The language must be en or ar.", nameof(language));
        }

        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is not { } household || household.FindActiveMembership(caller.UserId) is not { } membership)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var actable = await access.ActableMemberIdsAsync(cancellationToken);
        var members = household.Members
            .Where(member => actable.Contains(member.Id))
            .OrderBy(member => member.CreatedAt)
            .ThenBy(member => member.Id.Value)
            .ToList();
        var labels = members
            .Select((member, index) => (member.Id, Label: $"member_{index + 1}"))
            .ToDictionary(pair => pair.Id, pair => pair.Label);
        var self = household.FindMember(membership.FamilyMemberId);
        var isHead = await participant.IsHeadOfHouseholdAsync(cancellationToken);

        var assigned = await AllAsync<ParticipantAssignmentResponse>(page => assignments.HandleAsync(
            new MyAssignmentsRequest { Page = page.Page, PageSize = page.PageSize }, cancellationToken));
        var assignmentsById = assigned.ToDictionary(assignment => assignment.Id);

        return new FamilyContext(
            language,
            today,
            new FamilyContextCaller(
                labels.GetValueOrDefault(membership.FamilyMemberId, "member_unknown"),
                self?.RelationshipToHousehold,
                isHead,
                membership.Role == HouseholdRole.Manager),
            new FamilyContextHousehold(household.Reference.Value, household.Status, household.RosterConfirmedAt is not null),
            members.Select(member => new FamilyContextMember(
                labels[member.Id],
                member.RelationshipToHousehold,
                member.AgeOn(today),
                member.EmploymentStatus,
                member.IsHeadOfHousehold,
                member.IsPrimaryIncomeEarner,
                household.ActiveMembershipFor(member.Id) is not null,
                member.Id == membership.FamilyMemberId)).ToList(),
            (await AllAsync<ConsentRecordResponse>(page => consents.HandleAsync(page, cancellationToken)))
                .Where(consent => labels.ContainsKey(FamilyMemberId.From(consent.MemberId)))
                .Select(consent => new FamilyContextConsent(
                    labels[FamilyMemberId.From(consent.MemberId)], consent.ConsentType, consent.IsActive, consent.GrantedAt, consent.WithdrawnAt))
                .ToList(),
            assigned
                .Where(assignment => labels.ContainsKey(FamilyMemberId.From(assignment.MemberId)))
                .Select(assignment => new FamilyContextQuestionnaire(
                    labels[FamilyMemberId.From(assignment.MemberId)],
                    AssistantLanguages.Pick(assignment.InstrumentName, language),
                    assignment.SectionTitle is { } title ? AssistantLanguages.Pick(title, language) : null,
                    assignment.WaveCode,
                    assignment.Status,
                    assignment.AssignedAt,
                    assignment.ExpiresAt,
                    assignment.CompletedAt))
                .ToList(),
            await SessionsAsync(actable, labels, assignmentsById, isHead, language, cancellationToken),
            isHead ? ScoreVisibility.Visible : ScoreVisibility.HeadOfHouseholdOnly,
            (await AllAsync<RecommendationResponse>(page => recommendations.HandleAsync(page, cancellationToken)))
                .Where(recommendation => labels.ContainsKey(FamilyMemberId.From(recommendation.MemberId)))
                .Select(recommendation => new FamilyContextNextStep(
                    labels[FamilyMemberId.From(recommendation.MemberId)],
                    recommendation.CategoryCode,
                    recommendation.ReferralStatus,
                    recommendation.CreatedAt))
                .ToList());
    }

    private async Task<IReadOnlyList<FamilyContextSession>> SessionsAsync(
        IReadOnlyList<FamilyMemberId> actable,
        Dictionary<FamilyMemberId, string> labels,
        Dictionary<Guid, ParticipantAssignmentResponse> assignmentsById,
        bool isHead,
        string language,
        CancellationToken cancellationToken)
    {
        if (actable.Count == 0)
        {
            return [];
        }

        var loaded = new Dictionary<InstrumentVersionId, InstrumentVersion>();
        var built = new List<FamilyContextSession>();
        foreach (var session in await sessions.ListForMembersAsync(actable, cancellationToken))
        {
            if (!labels.TryGetValue(session.FamilyMemberId, out var label))
            {
                continue;
            }

            if (!loaded.TryGetValue(session.InstrumentVersionId, out var version))
            {
                version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
                    ?? throw new InvalidOperationException("A session's version must exist.");
                loaded[session.InstrumentVersionId] = version;
            }

            var assignment = assignmentsById.GetValueOrDefault(session.AssessmentAssignmentId.Value)
                ?? throw new InvalidOperationException("A session's assignment must be listed with its member's assignments.");
            built.Add(new FamilyContextSession(
                label,
                AssistantLanguages.Pick(assignment.InstrumentName, language),
                assignment.SectionTitle is { } title ? AssistantLanguages.Pick(title, language) : null,
                assignment.WaveCode,
                session.Status,
                session.StartedAt,
                session.SubmittedAt,
                Answers(session, version, language),
                isHead ? await ResultAsync(session, cancellationToken) : null));
        }

        return built;
    }

    /// <summary>Each answer as its question's prompt and the chosen option's label, in question order.</summary>
    private static List<FamilyContextAnswer> Answers(AssessmentSession session, InstrumentVersion version, string language)
    {
        var items = version.Sections
            .OrderBy(section => section.DisplayOrder)
            .SelectMany(section => section.Items.OrderBy(item => item.DisplayOrder))
            .Select((item, index) => (Item: item, Order: index))
            .ToDictionary(pair => pair.Item.Id);
        return session.Responses
            .Where(response => items.ContainsKey(response.InstrumentItemId))
            .OrderBy(response => items[response.InstrumentItemId].Order)
            .Select(response =>
            {
                var item = items[response.InstrumentItemId].Item;
                var option = item.FindOption(response.ResponseOptionId)
                    ?? throw new InvalidOperationException("An answer's option must belong to its item.");
                return new FamilyContextAnswer(AssistantLanguages.Pick(item.Prompt, language), AssistantLanguages.Pick(option.Label, language));
            })
            .ToList();
    }

    /// <summary>The session's latest result, read only for the head (the caller checks); null until it is scored.</summary>
    private async Task<FamilyContextResult?> ResultAsync(AssessmentSession session, CancellationToken cancellationToken)
    {
        if (session.Status != SessionStatus.Scored)
        {
            return null;
        }

        var latest = (await scoreResults.ListForSessionAsync(session.Id, cancellationToken)).MaxBy(result => result.ComputedAt);
        return latest is null ? null : new FamilyContextResult(latest.Status, latest.TotalScore, latest.TierCode);
    }

    /// <summary>Every item of a paged list, a page of the largest size at a time.</summary>
    private static async Task<IReadOnlyList<T>> AllAsync<T>(Func<PageRequest, Task<Result<PagedResponse<T>>>> list)
    {
        var items = new List<T>();
        for (var number = 1; ; number++)
        {
            var page = await list(new PageRequest { Page = number, PageSize = PageRequestValidator.MaxPageSize });
            if (page.IsFailure)
            {
                throw new InvalidOperationException($"A list in the family context failed: {page.Error!.Code}.");
            }

            items.AddRange(page.Value.Items);
            if (page.Value.Items.Count == 0 || items.Count >= page.Value.TotalCount)
            {
                return items;
            }
        }
    }
}
