namespace FamilyResilience.IdentityProvider;

/// <summary>The result of a refresh: the replacement refresh token, and the grant and scope for the new access token.</summary>
internal sealed record RefreshRotation(GrantOutcome Outcome, AuthorizationGrant? Grant, string? Scope, string? RefreshToken);
