using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A model's text about one fully answered area, usually before the session is submitted, so the report is finished by the
/// time the score exists (D-113). The answers come from the server's records. Nothing is stored or logged, and nothing in
/// the platform reads the text.
/// </summary>
public sealed class WriteScoreReportPart(
    ScoreReportPreparation preparation,
    ScoreReportModelCalls calls,
    IScoreReportModel model,
    ISupportKnowledgeGraph graph,
    TimeProvider timeProvider)
{
    public const string AuditAction = "assistant.score_report_part.generated";

    /// <summary>What a part's model call can afford: every option adds to the call that must beat the person's next tap.</summary>
    public const int MaxSupportOptions = 8;

    public async Task<Result<ScoreReportPartResponse>> HandleAsync(
        Guid sessionId, string sectionCode, ScoreReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await preparation.LoadAsync(sessionId, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<ScoreReportPartResponse>(prepared.Error!);
        }

        var section = prepared.Value.Sections.FirstOrDefault(candidate => candidate.Code == sectionCode);
        if (section is null)
        {
            return Result.Failure<ScoreReportPartResponse>(QuestionnaireErrors.SectionNotFound);
        }

        if (!ScoreReportPreparation.IsAnswered(prepared.Value.Session, section))
        {
            return Result.Failure<ScoreReportPartResponse>(AssistantErrors.ScoreReportNotReady);
        }

        var language = request.Language!;
        var answers = ScoreReportPreparation.Answers(prepared.Value, section, language);
        var now = timeProvider.GetUtcNow();
        var candidates = graph.Infer(answers, DateOnly.FromDateTime(now.UtcDateTime), MaxSupportOptions);
        var facts = new ScoreReportPartFacts(language, AssistantLanguages.Pick(section.Title, language), answers)
        {
            SupportOptions = candidates.Select(candidate => ScoreReportSupportOption.From(candidate, language)).ToList(),
        };
        var writing = (await calls.CallAsync(AuditAction, sessionId, token => model.WritePartAsync(facts, token), written => written.Limited().IsUsable(), cancellationToken))?.Limited();
        if (writing is null)
        {
            return Result.Failure<ScoreReportPartResponse>(AssistantErrors.ModelFailed);
        }

        var standing = ScoreReportPreparation.Standing(prepared.Value, section);
        var nextSteps = writing.Improvements
            .Select(improvement => ScoreReportNextStep.From(improvement, sectionCode, standing, language, candidates, graph))
            .ToList();
        return Result.Success(new ScoreReportPartResponse(
            sectionCode, language, writing.Strengths, writing.Weaknesses, nextSteps, model.ModelName, now, standing));
    }
}
