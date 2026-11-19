namespace FamilyResilience.Application.Assistant;

/// <summary>One strength or weakness in a report. <see cref="ToString"/> prints nothing about it.</summary>
public sealed record ScoreReportPoint(string Title, string Detail)
{
    public override string ToString() => nameof(ScoreReportPoint);
}
