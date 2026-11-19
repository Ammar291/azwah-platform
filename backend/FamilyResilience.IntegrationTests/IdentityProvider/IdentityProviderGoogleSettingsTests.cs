using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>The Google settings are refused unless complete and pointed at Google's own hosts (D-147).</summary>
public sealed class IdentityProviderGoogleSettingsTests
{
    private static IdentityProviderFactory DeployedWithGoogle() => new()
    {
        Deployed = true, UaePassEnabled = true, GoogleEnabled = true, SigningKeyPem = IdentityProviderFactory.TestRsaPem(),
    };

    private static string StartupFailure(Dictionary<string, string?> settings, bool deployed = false)
    {
        using var factory = (deployed ? DeployedWithGoogle() : new IdentityProviderFactory { GoogleEnabled = true }).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        return Describe(exception);
    }

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";

    [Theory]
    [InlineData("AuthorizationEndpoint", "https://accounts.google.com.attacker.example/o/oauth2/v2/auth", "accounts.google.com")]
    [InlineData("AuthorizationEndpoint", "http://accounts.google.com/o/oauth2/v2/auth", "accounts.google.com")]
    [InlineData("TokenEndpoint", "https://oauth2.googleapis.com:8443/token", "oauth2.googleapis.com")]
    [InlineData("TokenEndpoint", "https://user@oauth2.googleapis.com/token", "oauth2.googleapis.com")]
    [InlineData("UserInfoEndpoint", "https://localhost/v1/userinfo", "openidconnect.googleapis.com")]
    [InlineData("UserInfoEndpoint", "not a url", "openidconnect.googleapis.com")]
    public void It_refuses_any_google_endpoint_that_is_not_https_on_googles_host(string setting, string value, string host)
    {
        var failure = StartupFailure(new() { [$"IdentityProvider:Google:{setting}"] = value });

        Assert.Contains($"IdentityProvider:Google:{setting} must be an https URL on {host}", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(IdentityProviderFactory.GoogleClientSecret, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_a_scope_without_openid()
    {
        var failure = StartupFailure(new() { ["IdentityProvider:Google:Scope"] = "email profile" });

        Assert.Contains("IdentityProvider:Google:Scope", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ClientId", "IdentityProvider:Google:ClientId is required")]
    [InlineData("ClientSecret", "IdentityProvider:Google:ClientSecret is required")]
    [InlineData("ApprovalReference", "IdentityProvider:Google:ApprovalReference is required outside Development")]
    public void A_deployed_provider_refuses_to_enable_google_without_credentials_or_an_approval_reference(string setting, string expected)
    {
        var failure = StartupFailure(new() { [$"IdentityProvider:Google:{setting}"] = string.Empty }, deployed: true);

        Assert.Contains(expected, failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deployed_provider_starts_with_google_credentials_and_an_approval_reference()
    {
        await using var factory = DeployedWithGoogle();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task In_development_google_starts_without_an_approval_reference()
    {
        await using var factory = new IdentityProviderFactory { GoogleEnabled = true }.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IdentityProvider:Google:ApprovalReference"] = "" })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(IdentityProviderFactory.Issuer), AllowAutoRedirect = false });

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var metadata = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "google" }, metadata.GetProperty("azwah_identity_providers").EnumerateArray().Select(hint => hint.GetString()!));
    }

    [Fact]
    public async Task In_development_google_enabled_without_credentials_starts_with_google_off()
    {
        await using var factory = new IdentityProviderFactory { GoogleEnabled = true }.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityProvider:Google:ClientId"] = "",
                    ["IdentityProvider:Google:ClientSecret"] = "",
                })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(IdentityProviderFactory.Issuer), AllowAutoRedirect = false });
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["idp_hint"] = "google";

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("temporarily_unavailable", AuthorizationResponse.From(response.Headers.Location!)["error"]);
    }

    [Fact]
    public void Disabled_settings_are_not_checked()
    {
        using var factory = new IdentityProviderFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityProvider:Google:TokenEndpoint"] = "https://attacker.example/token",
                })));

        using var client = factory.CreateClient();
    }
}
