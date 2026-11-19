using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The checks and facts shared by everything that writes a report ahead of the score (D-113). The session must be one the
/// caller may act for, the caller the household's head (D-103) with the member's participation consent still active, and
/// an approved scoring rule set with bands must cover it, because only approved rules count (rule 9). Points use the same
/// rule the score will (<see cref="ScoringItem.PointsFor"/>), and every answer is read from the server's own records.
/// </summary>
public sealed class ScoreReportPreparation(
    IScoreReportModel model,
    IAssessmentSessionRepository sessions,
    IParticipantAccess access,
    IInstrumentVersionRepository versions,
    ScoreScaleReader scales,
    IHouseholdRepository households,
    IConsentRecordRepository consents)
{
    public async Task<Result<PreparedSession>> LoadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (!model.IsConfigured)
        {
            return Result.Failure<PreparedSession>(AssistantErrors.Unavailable);
        }

        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PreparedSession>(loaded.Error!);
        }

        if (!await access.IsHeadOfHouseholdAsync(cancellationToken))
        {
            return Result.Failure<PreparedSession>(AssistantErrors.ScoreReportHeadOnly);
        }

        var session = loaded.Value;
        var allowed = await ParticipationChecks.EnsureMayAnswerAsync(session.FamilyMemberId, households, consents, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<PreparedSession>(allowed.Error!);
        }

        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var sectionCode = session.InstrumentSectionId is { } sectionId ? version.FindSection(sectionId)?.Code : null;
        var content = ScoringContent.From(version, sectionCode);
        if (session.Status == SessionStatus.Abandoned
            || content.IsFailure
            || await scales.ReadApprovedAsync(version, sectionCode, cancellationToken) is null)
        {
            return Result.Failure<PreparedSession>(AssistantErrors.ScoreReportNotReady);
        }

        var inScope = version.Sections
            .Where(section => session.InstrumentSectionId is null || section.Id == session.InstrumentSectionId)
            .OrderBy(section => section.DisplayOrder)
            .ToList();
        return Result.Success(new PreparedSession(session, inScope, content.Value));
    }

    /// <summary>Whether every required question in <paramref name="section"/> has an answer on the server.</summary>
    public static bool IsAnswered(AssessmentSession session, InstrumentSection section) =>
        section.Items.Where(item => item.IsRequired).All(item => session.FindResponse(item.Id) is not null);

    /// <summary>Where the section's answers stand between its lowest and highest possible, by the approved rules' points.</summary>
    public static decimal Standing(PreparedSession prepared, InstrumentSection section)
    {
        var items = section.Items.Select(item => item.Id).ToHashSet();
        var scored = prepared.Content.ScoredItems().Where(item => items.Contains(item.ItemId)).ToList();
        var points = Answers(prepared, section, AssistantLanguages.English).Sum(answer => answer.Points ?? 0m);
        return ScoreReportNextStep.StandingOf(points, scored.Sum(item => item.ScaleMinimum()), scored.Sum(item => item.ScaleMaximum()));
    }

    /// <summary>The section's answers in question order, each with the points the approved rules would count.</summary>
    public static List<ScoreReportAnswer> Answers(PreparedSession prepared, InstrumentSection section, string language)
    {
        var area = AssistantLanguages.Pick(section.Title, language);
        var scored = prepared.Content.ScoredItems().ToDictionary(item => item.ItemId);
        var answers = new List<ScoreReportAnswer>();
        foreach (var item in section.Items.OrderBy(item => item.DisplayOrder))
        {
            var response = prepared.Session.FindResponse(item.Id);
            if (response is null || !scored.TryGetValue(item.Id, out var scoring))
            {
                continue;
            }

            var option = item.FindOption(response.ResponseOptionId)
                ?? throw new InvalidOperationException("An answer's option must belong to its item.");
            answers.Add(new ScoreReportAnswer(
                area,
                AssistantLanguages.Pick(item.Prompt, language),
                AssistantLanguages.Pick(option.Label, language),
                option.IsNonSubstantive ? null : scoring.PointsFor(response.RawValue),
                scoring.ScaleMaximum())
            {
                QuestionCode = item.Code,
                MinimumPoints = scoring.ScaleMinimum(),
            });
        }

        return answers;
    }
}
