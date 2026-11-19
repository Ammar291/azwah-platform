using System.Text.Json.Serialization;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One question with the chosen answer. <see cref="Points"/> is what the approved rules counted for it, reverse scoring
/// already applied, so more points always means stronger; null when the answer was outside the scale and not counted.
/// <see cref="QuestionCode"/> and <see cref="MinimumPoints"/> feed the knowledge graph and do not go to a model.
/// Sensitive: <see cref="ToString"/> prints nothing about it.
/// </summary>
public sealed record ScoreReportAnswer(string Area, string Question, string Answer, decimal? Points, int MaximumPoints)
{
    [JsonIgnore]
    public string? QuestionCode { get; init; }

    [JsonIgnore]
    public int MinimumPoints { get; init; }

    /// <summary>From 0 at the top of the scale to 1 at its bottom; 0 for an answer outside the scale.</summary>
    public decimal RoomToImprove() =>
        Points is not { } points || MaximumPoints <= MinimumPoints
            ? 0m
            : Math.Clamp((MaximumPoints - points) / (MaximumPoints - MinimumPoints), 0m, 1m);

    public override string ToString() => nameof(ScoreReportAnswer);
}
