namespace FamilyResilience.Application.Analytics;

/// <summary>Query string for score distributions: an optional wave. Nothing else can be filtered or grouped.</summary>
public sealed class ScoreDistributionsRequest
{
    public Guid? WaveId { get; init; }
}
