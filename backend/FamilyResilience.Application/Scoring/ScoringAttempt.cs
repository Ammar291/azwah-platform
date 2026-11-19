using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// What scoring a session did: recorded a new result (staged, not yet committed) or found the one the same approved rule
/// set had already recorded. <see cref="SectionCode"/> is the scope that was scored.
/// </summary>
public sealed record ScoringAttempt(ScoreResult Result, bool Created, string? SectionCode);
