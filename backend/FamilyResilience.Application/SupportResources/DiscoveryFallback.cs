namespace FamilyResilience.Application.SupportResources;

/// <summary>Why a language-model search answered with the deterministic result instead.</summary>
public enum DiscoveryFallback
{
    ModelTimeout,
    ModelUnavailable,
    ModelOutputInvalid,
    ModelOutputUngrounded,
    ModelSelectedNone,
}
