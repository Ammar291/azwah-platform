namespace FamilyResilience.IdentityProvider;

/// <summary>An authorization error for a registered redirect URI (RFC 6749 section 4.1.2.1).</summary>
internal sealed record RedirectError(string RedirectUri, string? State, string Error, string Description);
