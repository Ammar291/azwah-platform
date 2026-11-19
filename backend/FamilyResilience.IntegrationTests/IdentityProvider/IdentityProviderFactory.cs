using FamilyResilience.IdentityProvider;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// The development identity provider on a test server, in Development, with a known admin client secret and a clock
/// the test can move. Clients address it as its issuer (http://localhost:5090) and never follow redirects. UAE PASS is
/// always the <see cref="FakeUaePass"/> stub and Google the <see cref="FakeGoogle"/> stub, enabled or not, so no test
/// reaches the real services.
/// </summary>
public sealed class IdentityProviderFactory : WebApplicationFactory<IdentityProviderOptions>
{
    public const string Issuer = "http://localhost:5090";

    /// <summary>The deployed-mode issuer and clients (<see cref="Deployed"/>): https everywhere, no Expo Go addresses.</summary>
    public const string DeployedIssuer = "https://id.azwah.test";
    public const string DeployedAdminRedirectUri = "https://admin.azwah.test/auth/callback";
    public const string DeployedAdminPostLogoutUri = "https://admin.azwah.test/sign-in?signedOut=1";
    public const string AdminClientSecret = "test-only-admin-client-secret";
    public const string UaePassClientId = "test-uaepass-client";
    public const string UaePassClientSecret = "test-only-uaepass-secret";
    public const string GoogleClientId = "test-google-client.apps.googleusercontent.com";
    public const string GoogleClientSecret = "test-only-google-secret";
    public const string GoogleApprovalReference = "TEST-GOOGLE-APPROVAL";

    private static readonly Lazy<string> TestKeyPem = new(() =>
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    });

    /// <summary>One 2048-bit RSA private key (PKCS#8 PEM) per test run, for tests that configure the signing key.</summary>
    public static string TestRsaPem() => TestKeyPem.Value;

    public IdentityProviderFactory()
    {
        ClientOptions.AllowAutoRedirect = false;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.BaseAddress ??= new Uri(IssuerUrl);
        base.ConfigureClient(client);
    }

    /// <summary>The admin client's configured secret; empty means "not configured".</summary>
    public string ConfiguredAdminClientSecret { get; init; } = AdminClientSecret;

    /// <summary>Turns on the UAE PASS staging sign-in, with test-only client credentials.</summary>
    public bool UaePassEnabled { get; init; }

    /// <summary>
    /// Turns on the Google sign-in, with test-only client credentials and, deployed, a test approval reference.
    /// </summary>
    public bool GoogleEnabled { get; init; }

    /// <summary>
    /// <c>IdentityProvider:Google:StaffRoles</c> (D-149): the roles a Google sign-in for a staff client receives. Empty
    /// leaves the setting unset.
    /// </summary>
    public IReadOnlyList<string> GoogleStaffRoles { get; init; } = [];

    /// <summary><c>IdentityProvider:Clients:azwah-admin:GoogleStaffSignIn</c> (D-149).</summary>
    public bool AdminGoogleStaffSignIn { get; init; }

    /// <summary>The hosting environment name; anything but Development is the deployed mode.</summary>
    public string Environment { get; init; } = "Development";

    /// <summary>
    /// Run as a deployed provider: environment Staging, the https <see cref="DeployedIssuer"/>, and both clients
    /// registered in memory (no appsettings.Development.json is loaded outside Development) with https or app-scheme
    /// redirect URIs. Tests add <see cref="UaePassEnabled"/> and <see cref="SigningKeyPem"/> themselves.
    /// </summary>
    public bool Deployed { get; init; }

    /// <summary>The issuer this factory announces, whichever mode it runs in.</summary>
    public string IssuerUrl => Deployed ? DeployedIssuer : Issuer;

    /// <summary>A deployed provider with everything it needs to start: UAE PASS on and a configured signing key.</summary>
    public static IdentityProviderFactory DeployedFactory() => new() { Deployed = true, UaePassEnabled = true, SigningKeyPem = TestRsaPem() };

    /// <summary>The configured signing key (<c>IdentityProvider:SigningKey:Pem</c>), or null for a random key per process.</summary>
    public string? SigningKeyPem { get; init; }

    public AdjustableTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public FakeUaePass UaePass { get; } = new();

    public FakeGoogle Google { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Deployed && Environment == "Development" ? "Staging" : Environment);
        var settings = new Dictionary<string, string?>
        {
            ["IdentityProvider:Issuer"] = IssuerUrl,
            ["IdentityProvider:Clients:azwah-admin:Secret"] = ConfiguredAdminClientSecret,
            ["IdentityProvider:UaePass:Enabled"] = UaePassEnabled ? "true" : "false",
            ["IdentityProvider:Google:Enabled"] = GoogleEnabled ? "true" : "false",
        };
        if (Deployed)
        {
            settings["IdentityProvider:Clients:azwah-mobile:Confidential"] = "false";
            settings["IdentityProvider:Clients:azwah-mobile:RedirectUris:0"] = IdentityProviderFlow.MobileRedirectUri;
            settings["IdentityProvider:Clients:azwah-mobile:PostLogoutRedirectUris:0"] = "azwah://auth/signed-out";
            settings["IdentityProvider:Clients:azwah-mobile:AllowExpoGoRedirects"] = "false";
            settings["IdentityProvider:Clients:azwah-admin:Confidential"] = "true";
            settings["IdentityProvider:Clients:azwah-admin:RedirectUris:0"] = DeployedAdminRedirectUri;
            settings["IdentityProvider:Clients:azwah-admin:PostLogoutRedirectUris:0"] = DeployedAdminPostLogoutUri;
        }
        if (UaePassEnabled)
        {
            settings["IdentityProvider:UaePass:ClientId"] = UaePassClientId;
            settings["IdentityProvider:UaePass:ClientSecret"] = UaePassClientSecret;
        }

        if (GoogleEnabled)
        {
            settings["IdentityProvider:Google:ClientId"] = GoogleClientId;
            settings["IdentityProvider:Google:ClientSecret"] = GoogleClientSecret;
            settings["IdentityProvider:Google:ApprovalReference"] = GoogleApprovalReference;
        }

        for (var index = 0; index < GoogleStaffRoles.Count; index++)
        {
            settings[$"IdentityProvider:Google:StaffRoles:{index}"] = GoogleStaffRoles[index];
        }

        if (AdminGoogleStaffSignIn)
        {
            settings["IdentityProvider:Clients:azwah-admin:GoogleStaffSignIn"] = "true";
        }

        if (SigningKeyPem is not null)
        {
            settings["IdentityProvider:SigningKey:Pem"] = SigningKeyPem;
        }

        // Added last, so these win over appsettings and any user secrets on the machine running the tests.
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(UaePassClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(UaePass.CreateHandler);
            services.AddHttpClient(GoogleClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(Google.CreateHandler);
        });
    }
}
