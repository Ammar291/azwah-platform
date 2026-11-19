using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A model's summary of a fully answered session, written ahead of the score (D-113). It works from the answers, not from
/// the areas' text, so it can run beside the last area's call instead of after it. Nothing is stored or logged.
/// </summary>
public sealed class WriteScoreReportSummary(
    ScoreReportPreparation preparation, ScoreReportModelCalls calls, IScoreReportModel model, TimeProvider timeProvider)
{
    public const string AuditAction = "assistant.score_report_summary.generated";

    public async Task<Result<ScoreReportSummaryResponse>> HandleAsync(Guid sessionId, ScoreReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await preparation.LoadAsync(sessionId, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<ScoreReportSummaryResponse>(prepared.Error!);
        }

        var session = prepared.Value;
        if (!session.Sections.All(section => ScoreReportPreparation.IsAnswered(session.Session, section)))
        {
            return Result.Failure<ScoreReportSummaryResponse>(AssistantErrors.ScoreReportNotReady);
        }

        var language = request.Language!;
        var facts = Facts(session, language);
        var writing = await calls.CallAsync(AuditAction, sessionId, token => model.WriteSummaryAsync(facts, token), written => written.IsUsable(), cancellationToken);
        return writing is null
            ? Result.Failure<ScoreReportSummaryResponse>(AssistantErrors.ModelFailed)
            : Result.Success(new ScoreReportSummaryResponse(
                language, ScoreReportWordBudget.Trim(writing.Summary, ScoreReportWordBudget.SummaryWords), model.ModelName, timeProvider.GetUtcNow()));
    }

    private static ScoreReportSummaryFacts Facts(PreparedSession session, string language)
    {
        var answers = new List<ScoreReportAnswer>();
        var areas = new List<ScoreReportArea>();
        foreach (var section in session.Sections)
        {
            var inSection = ScoreReportPreparation.Answers(session, section, language);
            answers.AddRange(inSection);
            var codes = section.Items.Select(item => item.Id).ToHashSet();
            var items = session.Content.ScoredItems().Where(item => codes.Contains(item.ItemId)).ToList();
            areas.Add(new ScoreReportArea(
                AssistantLanguages.Pick(section.Title, language),
                inSection.Sum(answer => answer.Points ?? 0m),
                items.Sum(item => item.ScaleMinimum()),
                items.Sum(item => item.ScaleMaximum())));
        }

        return new ScoreReportSummaryFacts(language, areas, answers);
    }
}
