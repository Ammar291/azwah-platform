namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// The answer to a search. <see cref="Notice"/> is always <see cref="DiscoveryNotice.InformationOnly"/>. Every match is a
/// real directory entry; its ID and URL are the source. <see cref="SafetyNotice"/> is <c>crisis_support</c> when a
/// participant's text matched the crisis phrase screen (D-138), otherwise null.
/// </summary>
public sealed record SupportResourceDiscoveryResponse(
    DiscoveryOutcome Outcome,
    DiscoveryMode Mode,
    DiscoveryFallback? Fallback,
    DiscoveryNotice Notice,
    IReadOnlyList<SupportResourceMatchResponse> Matches,
    string? SafetyNotice);
