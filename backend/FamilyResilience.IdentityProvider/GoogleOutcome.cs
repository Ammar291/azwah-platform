namespace FamilyResilience.IdentityProvider;

/// <summary>The identity Google returned, or a fixed reason code (safe to log) for why there is none.</summary>
internal sealed record GoogleOutcome(GoogleIdentity? Identity, string? FailureReason)
{
    public static GoogleOutcome Success(GoogleIdentity identity) => new(identity, null);

    public static GoogleOutcome Failure(string reason) => new(null, reason);
}
