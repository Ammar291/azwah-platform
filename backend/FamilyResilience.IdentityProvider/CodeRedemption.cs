namespace FamilyResilience.IdentityProvider;

/// <summary>The result of presenting an authorization code; <see cref="CodeKey"/> names the redeemed code in the store.</summary>
internal sealed record CodeRedemption(GrantOutcome Outcome, AuthorizationGrant? Grant, string? CodeKey);
