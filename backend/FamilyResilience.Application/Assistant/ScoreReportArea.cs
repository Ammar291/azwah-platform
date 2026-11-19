namespace FamilyResilience.Application.Assistant;

/// <summary>One area's recorded score and the lowest and highest it could be. <see cref="ToString"/> prints nothing about it.</summary>
public sealed record ScoreReportArea(string Name, decimal Score, int Minimum, int Maximum)
{
    /// <summary>The section's code, for a model to say which area an improvement is about.</summary>
    public string Code { get; init; } = string.Empty;

    public override string ToString() => nameof(ScoreReportArea);
}
