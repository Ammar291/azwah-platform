namespace FamilyResilience.Domain.Safeguarding;

/// <summary>What noticed the signal. Wire codes: phrase_screen, ai_moderation, provider_filter, score_band.</summary>
public enum SafetyDetector
{
    PhraseScreen = 1,
    AiModeration = 2,
    ProviderFilter = 3,
    ScoreBand = 4,
}
