namespace FamilyResilience.IdentityProvider;

/// <summary>The identity UAE PASS returned, or a fixed reason code (safe to log) for why there is none.</summary>
internal sealed record UaePassOutcome(UaePassIdentity? Identity, string? FailureReason)
{
    public static UaePassOutcome Success(UaePassIdentity identity) => new(identity, null);

    public static UaePassOutcome Failure(string reason) => new(null, reason);
}
