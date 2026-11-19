namespace FamilyResilience.Domain.Analytics;

/// <summary>What a research export lists: one row per submitted session, or one per score result.</summary>
public enum ExportDataset
{
    AssessmentSessions = 1,
    ScoreResults = 2,
}
