namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Settings under "IdentityProvider". Also the public marker type that tests pass to <c>WebApplicationFactory</c>.
/// </summary>
public sealed class IdentityProviderOptions
{
    public const string SectionName = "IdentityProvider";

    /// <summary>
    /// The issuer identifier in discovery, tokens and authorization responses. It never follows the request's host:
    /// every client (browser, phone, API) must reach the provider under this same URL.
    /// </summary>
    public string Issuer { get; set; } = "http://localhost:5090";

    /// <summary>The access tokens' <c>aud</c>: the API's audience.</summary>
    public string Audience { get; set; } = "azwah-api";

    public int AccessTokenSeconds { get; set; } = 600;

    /// <summary>Registered clients by client ID.</summary>
    public Dictionary<string, IdentityProviderClientOptions> Clients { get; } = new(StringComparer.Ordinal);

    /// <summary>The optional upstream sign-in with a UAE PASS staging test account (off by default).</summary>
    public UaePassOptions UaePass { get; set; } = new();

    /// <summary>The optional upstream sign-in with a Google account (off by default, D-147).</summary>
    public GoogleOptions Google { get; set; } = new();

    /// <summary>The RS256 signing key; required outside Development.</summary>
    public SigningKeyOptions SigningKey { get; set; } = new();
}
