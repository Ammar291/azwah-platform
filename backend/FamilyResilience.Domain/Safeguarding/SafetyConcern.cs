namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// What the signal is about. Wire codes: suicide_or_self_harm, harm_to_others, abuse_or_danger, low_resilience_score.
/// </summary>
public enum SafetyConcern
{
    SuicideOrSelfHarm = 1,
    HarmToOthers = 2,
    AbuseOrDanger = 3,
    LowResilienceScore = 4,
}
