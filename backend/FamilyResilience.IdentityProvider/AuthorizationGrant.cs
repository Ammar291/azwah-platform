namespace FamilyResilience.IdentityProvider;

/// <summary>
/// What a sign-in granted: bound to an authorization code, then to a refresh token family. <see cref="Profile"/> is set
/// only for an upstream (UAE PASS staging) sign-in; it is already masked and goes into ID tokens, never access tokens.
/// </summary>
internal sealed record AuthorizationGrant(
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string Nonce,
    string Scope,
    Persona Persona,
    DateTimeOffset AuthTime,
    UpstreamProfile? Profile = null)
{
    public const string OfflineAccess = "offline_access";

    public bool AllowsRefresh => Scope.Split(' ').Contains(OfflineAccess, StringComparer.Ordinal);
}
