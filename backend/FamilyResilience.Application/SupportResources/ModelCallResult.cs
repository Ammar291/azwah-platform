namespace FamilyResilience.Application.SupportResources;

/// <summary>The result of one attempt to call a language model, as counted by <see cref="SupportResourceMetrics"/>.</summary>
public enum ModelCallResult
{
    Ok,
    Timeout,
    Unavailable,
    Invalid,
    Ungrounded,
}
