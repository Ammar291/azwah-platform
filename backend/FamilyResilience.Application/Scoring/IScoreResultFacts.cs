using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>Reads what a recorded score result says (<see cref="ScoreResultFacts"/>), for modules that react to one.</summary>
public interface IScoreResultFacts
{
    /// <summary>The result's facts; null when no such result exists (for example, its household was erased).</summary>
    Task<ScoreResultFacts?> GetAsync(ScoreResultId id, CancellationToken cancellationToken = default);
}
