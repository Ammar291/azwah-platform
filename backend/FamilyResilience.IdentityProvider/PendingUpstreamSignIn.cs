namespace FamilyResilience.IdentityProvider;

/// <summary>A validated authorization request waiting for an upstream sign-in, with the upstream and its PKCE verifier (if any).</summary>
internal sealed record PendingUpstreamSignIn(AuthorizationRequest Request, string Upstream, string? CodeVerifier);
