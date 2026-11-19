namespace FamilyResilience.IdentityProvider;

/// <summary>A client as registered in configuration.</summary>
internal sealed record RegisteredClient(
    string Id,
    bool Confidential,
    string? Secret,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris,
    bool AllowExpoGoRedirects,
    bool GoogleStaffSignIn = false)
{
    public const string ExpoGoCallbackSuffix = "/--/auth/callback";
    public const string ExpoGoSignedOutSuffix = "/--/auth/signed-out";

    public bool HasRedirectUri(string uri) =>
        RedirectUris.Contains(uri, StringComparer.Ordinal) || (AllowExpoGoRedirects && IsExpoGoUri(uri, ExpoGoCallbackSuffix));

    public bool HasPostLogoutRedirectUri(string uri) =>
        PostLogoutRedirectUris.Contains(uri, StringComparer.Ordinal)
        || (AllowExpoGoRedirects && IsExpoGoUri(uri, ExpoGoSignedOutSuffix));

    /// <summary>
    /// <c>exp://{host}[:port][/path]/--/auth/…</c> exactly: printable ASCII, no user info, query or fragment. Such an
    /// address only opens Expo Go on the phone, whichever host it names.
    /// </summary>
    private static bool IsExpoGoUri(string uri, string suffix) =>
        uri.StartsWith("exp://", StringComparison.Ordinal)
        && uri.EndsWith(suffix, StringComparison.Ordinal)
        && uri.Length > "exp://".Length + suffix.Length
        && uri.All(character => character is > ' ' and < '\u007f' and not ('?' or '#' or '@' or '\\' or '"' or '<' or '>'))
        && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == "exp"
        && parsed.Host.Length > 0;
}
