namespace FamilyResilience.IdentityProvider;

/// <summary>
/// A validated authorization request (authorization code flow with S256 PKCE, state and nonce). <see cref="UiLocales"/>
/// and <see cref="IdpHint"/> are optional: <c>idp_hint=uae_pass</c> asks for the UAE PASS staging sign-in instead of
/// the persona page, and <c>idp_hint=google</c> for the Google sign-in (D-147).
/// </summary>
internal sealed record AuthorizationRequest(
    RegisteredClient Client,
    string RedirectUri,
    string RequestedScope,
    string GrantedScope,
    string State,
    string Nonce,
    string CodeChallenge,
    string? UiLocales = null,
    string? IdpHint = null)
{
    public const string UaePassHint = "uae_pass";

    public const string GoogleHint = "google";

    public bool AsksForUaePass => string.Equals(IdpHint, UaePassHint, StringComparison.Ordinal);

    public bool AsksForGoogle => string.Equals(IdpHint, GoogleHint, StringComparison.Ordinal);
}
