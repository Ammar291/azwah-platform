namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Settings under "IdentityProvider:Google": the optional sign-in with a Google account (D-147), next to UAE PASS and,
/// in Development, the personas. The client ID and secret come from the owner's Google Cloud OAuth client and have no
/// defaults; set them in user secrets or environment variables, never in a committed file. Every endpoint must be https
/// on Google's own hosts. Outside Development the sign-in also needs <see cref="ApprovalReference"/>: Google accounts are
/// real people, not test identities.
/// </summary>
public sealed class GoogleOptions
{
    public const string AuthorizationHost = "accounts.google.com";
    public const string TokenHost = "oauth2.googleapis.com";
    public const string UserInfoHost = "openidconnect.googleapis.com";

    /// <summary>Where Google returns the browser, under the issuer: the callback URI is Issuer + this path.</summary>
    public const string CallbackPath = "/google/callback";

    public bool Enabled { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// The owner's approval for signing real Google accounts in to a deployed provider (open question 73). Required
    /// outside Development; ignored in Development.
    /// </summary>
    public string? ApprovalReference { get; set; }

    public string AuthorizationEndpoint { get; set; } = $"https://{AuthorizationHost}/o/oauth2/v2/auth";

    public string TokenEndpoint { get; set; } = $"https://{TokenHost}/token";

    public string UserInfoEndpoint { get; set; } = $"https://{UserInfoHost}/v1/userinfo";

    /// <summary>The account's stable subject, name and e-mail address (masked before it reaches a token).</summary>
    public string Scope { get; set; } = "openid email profile";

    /// <summary>
    /// Google staff sign-in for the demonstration stack (D-149), Development only: the roles a Google sign-in receives,
    /// instead of <c>participant</c>, when the client opts in with
    /// <see cref="IdentityProviderClientOptions.GoogleStaffSignIn"/>. Each must be one of <see cref="AssignableStaffRoles"/>.
    /// Empty (the default) turns the feature off; outside Development start-up refuses anything but empty. Whoever the
    /// owner's Google OAuth client lets sign in (its test users while it is in Testing) becomes staff.
    /// </summary>
    public List<string> StaffRoles { get; } = [];

    /// <summary>
    /// The platform's staff roles (the API's <c>Roles.All</c> without <c>participant</c>; the provider references no
    /// platform project, so a test keeps the two lists equal).
    /// </summary>
    public static IReadOnlyList<string> AssignableStaffRoles { get; } =
        ["system_administrator", "program_administrator", "researcher", "support_coordinator"];

    /// <summary>
    /// Enabled with a client ID and secret. In Development, enabled without credentials stays off (a start-up warning)
    /// so a fresh checkout starts; deployed, the validator refuses it.
    /// </summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static string CallbackUri(string issuer) => $"{issuer.TrimEnd('/')}{CallbackPath}";

    /// <summary>
    /// The staff roles a Google sign-in for <paramref name="client"/> receives (D-149), once each in configured order, or
    /// null for the usual participant sign-in: only in Development, only for a client that opts in, and only while
    /// <see cref="StaffRoles"/> lists any.
    /// </summary>
    internal IReadOnlyList<string>? StaffRolesFor(RegisteredClient client, ProviderMode mode) =>
        mode.PersonasAvailable && client.GoogleStaffSignIn && StaffRoles.Count > 0
            ? [.. StaffRoles.Distinct(StringComparer.Ordinal)]
            : null;
}
