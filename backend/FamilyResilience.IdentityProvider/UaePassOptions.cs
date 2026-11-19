namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Settings under "IdentityProvider:UaePass": the UAE PASS <b>staging</b> sign-in (the only sign-in outside Development).
/// The client ID and secret have no defaults; set them in user secrets or environment variables, never in a committed
/// file. Every endpoint must be https on <see cref="StagingHost"/>: production UAE PASS is refused until onboarding.
/// </summary>
public sealed class UaePassOptions
{
    /// <summary>The only host the provider talks to. Staging identities are test accounts; production ones are real people.</summary>
    public const string StagingHost = "stg-id.uaepass.ae";

    /// <summary>Where UAE PASS returns the browser, under the issuer: the callback URI is Issuer + this path.</summary>
    public const string CallbackPath = "/uaepass/callback";

    /// <summary>Where UAE PASS logout returns the browser (with the pending sign-out key as <c>r</c>).</summary>
    public const string SignedOutPath = "/uaepass/signed-out";

    public bool Enabled { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string AuthorizationEndpoint { get; set; } = $"https://{StagingHost}/idshub/authorize";

    public string TokenEndpoint { get; set; } = $"https://{StagingHost}/idshub/token";

    public string UserInfoEndpoint { get; set; } = $"https://{StagingHost}/idshub/userinfo";

    /// <summary>Where the browser is sent to end the UAE PASS session at sign-out (D-129).</summary>
    public string LogoutEndpoint { get; set; } = $"https://{StagingHost}/idshub/logout";

    /// <summary>
    /// Enabled with a client ID and secret. In Development, enabled without credentials stays off (a start-up
    /// warning) so a fresh checkout starts; deployed, the validator refuses it.
    /// </summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string Scope { get; set; } = "urn:uae:digitalid:profile:general";

    /// <summary>The web authentication level: the UAE PASS app approves the sign-in on the phone.</summary>
    public string AcrValues { get; set; } = "urn:safelayer:tws:policies:authentication:level:low";

    public static string CallbackUri(string issuer) => $"{issuer.TrimEnd('/')}{CallbackPath}";

    public static string SignedOutUri(string issuer) => $"{issuer.TrimEnd('/')}{SignedOutPath}";
}
