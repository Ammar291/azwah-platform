namespace FamilyResilience.Application.Assistant;

/// <summary>A report's summary as a model wrote it. <see cref="ToString"/> prints nothing about it.</summary>
public sealed record ScoreReportSummaryWriting(string Summary)
{
    public bool IsUsable() => !string.IsNullOrWhiteSpace(Summary) && Summary.Length <= ScoreReportWriting.MaxSummaryLength;

    public override string ToString() => nameof(ScoreReportSummaryWriting);
}
