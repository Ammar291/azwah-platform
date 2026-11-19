using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A written report on a session's recorded score, for the household's head only (D-103, D-112). Without a configured
/// model it answers <c>assistant.unavailable</c> and reads nothing. The facts come from the platform's records, go to the
/// model under <see cref="AssistantLimits.ScoreReportTimeout"/>, and every send is audited
/// (<c>assistant.score_report.generated</c>, the session, the outcome, no text), also when the person leaves while the
/// model writes. Any model failure, fault, time-out or unusable text is <c>assistant.model_failed</c>. Nothing is stored or
/// logged, and the model's text changes no record.
/// </summary>
public sealed class GenerateScoreReport(
    IScoreReportModel model,
    IAssessmentSessionRepository sessions,
    IParticipantAccess access,
    IScoreResultRepository scoreResults,
    ScoreScaleReader scales,
    IInstrumentVersionRepository versions,
    IAuditLog auditLog,
    ISupportKnowledgeGraph graph,
    AssistantLimits limits,
    TimeProvider timeProvider)
{
    public const string AuditAction = "assistant.score_report.generated";

    /// <summary>A whole report is written at once, so each area offers fewer options than a part's call does.</summary>
    public const int MaxSupportOptionsPerArea = 5;

    public async Task<Result<ScoreReportResponse>> HandleAsync(
        Guid sessionId, ScoreReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!model.IsConfigured)
        {
            return Result.Failure<ScoreReportResponse>(AssistantErrors.Unavailable);
        }

        var loaded = await SessionLoads.AccessibleAsync(sessions, access, sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<ScoreReportResponse>(loaded.Error!);
        }

        if (!await access.IsHeadOfHouseholdAsync(cancellationToken))
        {
            return Result.Failure<ScoreReportResponse>(AssistantErrors.ScoreReportHeadOnly);
        }

        var session = loaded.Value;
        var latest = session.Status == SessionStatus.Scored
            ? (await scoreResults.ListForSessionAsync(session.Id, cancellationToken)).MaxBy(result => result.ComputedAt)
            : null;
        if (latest is not { Status: ScoreStatus.Complete, TotalScore: { } total, TierCode: { } band }
            || await scales.ReadAsync(latest, cancellationToken) is not { } scale)
        {
            return Result.Failure<ScoreReportResponse>(AssistantErrors.ScoreReportNotReady);
        }

        var language = request.Language!;
        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var (facts, candidates) = Facts(session, latest, version, language, total, band, scale, today);

        ScoreReportWriting? writing;
        try
        {
            writing = await AskAsync(facts, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The facts have left the platform even though nobody is waiting for the answer, so this still leaves a record.
            await auditLog.RecordAsync(new AuditRecord(AuditAction, "assessment_session", sessionId.ToString(), AuditOutcome.Failed), CancellationToken.None);
            throw;
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord(AuditAction, "assessment_session", sessionId.ToString(), writing is null ? AuditOutcome.Failed : AuditOutcome.Succeeded),
            cancellationToken);
        if (recorded.IsFailure)
        {
            return Result.Failure<ScoreReportResponse>(recorded.Error!);
        }

        if (writing is null)
        {
            return Result.Failure<ScoreReportResponse>(AssistantErrors.ModelFailed);
        }

        var now = timeProvider.GetUtcNow();
        return Result.Success(new ScoreReportResponse(
            sessionId,
            language,
            total,
            band,
            scale,
            writing.Summary.Trim(),
            writing.Strengths,
            writing.Weaknesses,
            NextSteps(writing, facts, candidates, language),
            model.ModelName,
            now));
    }

    /// <summary>
    /// A whole report's improvements as steps, the weakest area first. A code the model got wrong gets no authorities and
    /// sorts last: the step's own words are still useful, and an authority is never guessed.
    /// </summary>
    private List<ScoreReportNextStep> NextSteps(
        ScoreReportWriting writing, ScoreReportFacts facts, Dictionary<string, IReadOnlyList<SupportCandidate>> candidates, string language)
    {
        var areas = facts.Areas.Where(area => area.Code.Length > 0).ToDictionary(area => area.Code, StringComparer.Ordinal);
        return writing.Improvements
            .Select(improvement =>
            {
                var code = improvement.SectionCode ?? string.Empty;
                return areas.TryGetValue(code, out var area)
                    ? ScoreReportNextStep.From(improvement, code, ScoreReportNextStep.StandingOf(area.Score, area.Minimum, area.Maximum), language, candidates.GetValueOrDefault(code) ?? [], graph)
                    : new ScoreReportNextStep(string.Empty, improvement.Area, improvement.Reason, improvement.Steps, 1m, []);
            })
            .OrderBy(step => step.Standing)
            .Take(ScoreReportWordBudget.MaxNextSteps)
            .ToList();
    }

    private (ScoreReportFacts Facts, Dictionary<string, IReadOnlyList<SupportCandidate>> Candidates) Facts(
        AssessmentSession session, ScoreResult result, InstrumentVersion version, string language, decimal total, string band, ScoreScaleResponse scale, DateOnly today)
    {
        var candidates = new Dictionary<string, IReadOnlyList<SupportCandidate>>(StringComparer.Ordinal);
        var inScope = version.Sections
            .Where(section => session.InstrumentSectionId is null || section.Id == session.InstrumentSectionId)
            .OrderBy(section => section.DisplayOrder)
            .ToList();
        var sectionCode = session.InstrumentSectionId is { } sectionId ? version.FindSection(sectionId)?.Code : null;
        var content = ScoringContent.From(version, sectionCode);
        var scored = content.IsSuccess
            ? content.Value.ScoredItems().ToDictionary(item => item.ItemId)
            : [];
        var contributions = result.Contributions.ToDictionary(contribution => contribution.InstrumentItemId);

        var answers = new List<ScoreReportAnswer>();
        var areas = new List<ScoreReportArea>();
        foreach (var section in inScope)
        {
            var area = AssistantLanguages.Pick(section.Title, language);
            var first = answers.Count;
            foreach (var item in section.Items.OrderBy(item => item.DisplayOrder))
            {
                var response = session.FindResponse(item.Id);
                if (response is null
                    || !contributions.TryGetValue(item.Id, out var contribution)
                    || !scored.TryGetValue(item.Id, out var scoring))
                {
                    continue;
                }

                var option = item.FindOption(response.ResponseOptionId)
                    ?? throw new InvalidOperationException("An answer's option must belong to its item.");
                answers.Add(new ScoreReportAnswer(
                    area,
                    AssistantLanguages.Pick(item.Prompt, language),
                    AssistantLanguages.Pick(option.Label, language),
                    contribution.IsExcluded ? null : contribution.CalculatedValue,
                    scoring.ScaleMaximum())
                {
                    QuestionCode = item.Code,
                    MinimumPoints = scoring.ScaleMinimum(),
                });
            }

            candidates[section.Code] = graph.Infer(answers.Skip(first).ToList(), today, MaxSupportOptionsPerArea);

            // By section, as the area-at-a-time path does, so a section without a scoring category still has its next step.
            var items = section.Items.Where(item => scored.ContainsKey(item.Id)).Select(item => scored[item.Id]).ToList();
            areas.Add(new ScoreReportArea(
                area,
                answers.Skip(first).Sum(answer => answer.Points ?? 0m),
                items.Sum(item => item.ScaleMinimum()),
                items.Sum(item => item.ScaleMaximum()))
            {
                Code = section.Code,
            });
        }

        var options = candidates
            .SelectMany(area => area.Value.Select(candidate => ScoreReportSupportOption.From(candidate, language, area.Key)))
            .ToList();
        return (new ScoreReportFacts(language, total, scale.Minimum, scale.Maximum, band, scale.Bands, areas, answers) { SupportOptions = options }, candidates);
    }

    /// <summary>The usable text, or null when the model failed, threw, wrote nothing usable or ran out of time.</summary>
    private async Task<ScoreReportWriting?> AskAsync(ScoreReportFacts facts, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.ScoreReportTimeout);
        try
        {
            // WaitAsync bounds the call even when an adapter ignores its token.
            var writing = await model.WriteAsync(facts, timeout.Token).WaitAsync(timeout.Token);
            var limited = writing.IsSuccess ? writing.Value.Limited() : null;
            return limited is not null && limited.IsUsable() ? limited : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Broad on purpose: a time-out, a provider fault or an adapter bug all mean the same to the person (502).
            // The exception is dropped unlogged, because an adapter's message could quote the request.
            return null;
        }
    }
}
