namespace FamilyResilience.Application.Scoring;

/// <summary>The result, and whether this call created it (false when the same rule set had already scored the session).</summary>
public sealed record ScoreSessionOutcome(ScoreResultResponse Result, bool Created);
