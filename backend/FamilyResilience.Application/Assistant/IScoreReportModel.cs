using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The text model that explains a recorded score in words (D-112). It receives the facts the platform already decided
/// and returns only text; it never produces a score, band or eligibility decision (CONTRIBUTING.md rules 1 and 7). The adapter
/// lives in Infrastructure, keeps its key in user secrets, honours the token, and logs at most a fixed code: never the
/// facts, the text or anything the provider sends back.
/// </summary>
public interface IScoreReportModel
{
    /// <summary>Whether a model is configured; false makes the API answer 503 <c>assistant.unavailable</c>.</summary>
    bool IsConfigured { get; }

    /// <summary>The model's name, recorded on the report so a reader knows what wrote it.</summary>
    string ModelName { get; }

    /// <summary>The report's text, or a failure. Exceptions and time-outs also count as failures (502).</summary>
    Task<Result<ScoreReportWriting>> WriteAsync(ScoreReportFacts facts, CancellationToken cancellationToken);

    /// <summary>The text about one area, written as the person answers (D-113). Failures as above.</summary>
    Task<Result<ScoreReportPartWriting>> WritePartAsync(ScoreReportPartFacts facts, CancellationToken cancellationToken);

    /// <summary>A report's summary, written as the person answers (D-113). Failures as above.</summary>
    Task<Result<ScoreReportSummaryWriting>> WriteSummaryAsync(ScoreReportSummaryFacts facts, CancellationToken cancellationToken);
}
