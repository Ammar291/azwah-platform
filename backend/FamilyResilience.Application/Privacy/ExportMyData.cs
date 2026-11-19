using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Privacy;

/// <summary>
/// The caller's copy of their data (D-89): their household, the members they may act for (D-49), and those members'
/// consents, assignments, sessions with every answer, referrals and privacy requests. One read, audited as
/// <c>privacy.data_export.read</c>. Scores, tiers and recommendations are left out and named in <c>notIncluded</c>.
/// </summary>
public sealed class ExportMyData(
    GetMe getMe,
    ICallerAccess access,
    ListMyConsents consents,
    ListMyAssignments assignments,
    ListMyReferrals referrals,
    IAssessmentSessionRepository sessions,
    IInstrumentVersionRepository versions,
    IPrivacyRequestRepository privacyRequests,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public static IReadOnlyList<string> NotIncluded { get; } = ["score_results", "support_recommendations"];

    public async Task<Result<MyDataExportResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var me = await getMe.HandleAsync(cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure<MyDataExportResponse>(me.Error!);
        }

        var caller = await access.GetCallerAsync(cancellationToken);
        var actable = await access.ActableMemberIdsAsync(cancellationToken);
        var members = caller?.Household is { } household
            ? household.Members.Where(member => actable.Contains(member.Id))
                .Select(member => member.ToResponse(household, caller.UserId, DateOnly.FromDateTime(now.UtcDateTime)))
                .ToList()
            : [];

        var exportedSessions = new List<MyDataSessionResponse>();
        if (actable.Count > 0)
        {
            var loaded = new Dictionary<InstrumentVersionId, InstrumentVersion>();
            foreach (var session in await sessions.ListForMembersAsync(actable, cancellationToken))
            {
                if (!loaded.TryGetValue(session.InstrumentVersionId, out var version))
                {
                    version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
                        ?? throw new InvalidOperationException("A session's version must exist.");
                    loaded[session.InstrumentVersionId] = version;
                }

                exportedSessions.Add(new MyDataSessionResponse(session.ToResponse(version), session.ToAnswers(version).Responses));
            }
        }

        var export = new MyDataExportResponse(
            now,
            me.Value.Subject,
            me.Value.Household,
            members,
            await AllAsync<ConsentRecordResponse>(page => consents.HandleAsync(page, cancellationToken)),
            await AllAsync<ParticipantAssignmentResponse>(page => assignments.HandleAsync(
                new MyAssignmentsRequest { Page = page.Page, PageSize = page.PageSize }, cancellationToken)),
            exportedSessions,
            await AllAsync<ReferralResponse>(page => referrals.HandleAsync(page, cancellationToken)),
            actable.Count == 0
                ? []
                : (await privacyRequests.ListForMembersAsync(actable, 0, int.MaxValue, cancellationToken)).Select(item => item.ToResponse()).ToList(),
            NotIncluded);

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("privacy.data_export.read", "data_export", caller?.Household?.Id.Value.ToString(), AuditOutcome.Succeeded),
            cancellationToken);
        return recorded.IsFailure ? Result.Failure<MyDataExportResponse>(recorded.Error!) : Result.Success(export);
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
                throw new InvalidOperationException($"A list in the data export failed: {page.Error!.Code}.");
            }

            items.AddRange(page.Value.Items);
            if (page.Value.Items.Count == 0 || items.Count >= page.Value.TotalCount)
            {
                return items;
            }
        }
    }
}
