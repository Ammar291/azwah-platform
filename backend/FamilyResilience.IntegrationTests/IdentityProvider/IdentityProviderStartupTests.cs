using System.Net;
using FamilyResilience.IdentityProvider;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderStartupTests
{
    [Fact]
    public async Task It_starts_outside_development_with_uae_pass_https_and_a_signing_key()
    {
        await using var factory = IdentityProviderFactory.DeployedFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("uae_pass", "IdentityProvider:UaePass:Enabled")]
    [InlineData("https_issuer", "IdentityProvider:Issuer")]
    [InlineData("signing_key", "IdentityProvider:SigningKey:Pem")]
    [InlineData("http_redirect", "IdentityProvider:Clients:azwah-admin")]
    [InlineData("expo_go", "IdentityProvider:Clients:azwah-mobile")]
    [InlineData("admin_secret", "IdentityProvider:Clients:azwah-admin:Secret")]
    public void It_refuses_to_start_deployed_without(string missing, string expectedSetting)
    {
        var overrides = new Dictionary<string, string?>();
        var factory = new IdentityProviderFactory
        {
            Deployed = true,
            UaePassEnabled = missing != "uae_pass",
            SigningKeyPem = missing == "signing_key" ? null : IdentityProviderFactory.TestRsaPem(),
            ConfiguredAdminClientSecret = missing == "admin_secret" ? "" : IdentityProviderFactory.AdminClientSecret,
        };
        switch (missing)
        {
            case "https_issuer":
                overrides["IdentityProvider:Issuer"] = "http://id.azwah.test";
                break;
            case "http_redirect":
                overrides["IdentityProvider:Clients:azwah-admin:RedirectUris:0"] = "http://admin.azwah.test/auth/callback";
                break;
            case "expo_go":
                overrides["IdentityProvider:Clients:azwah-mobile:AllowExpoGoRedirects"] = "true";
                break;
        }

        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(overrides)));
        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(expectedSetting, Describe(exception), StringComparison.Ordinal);
        Assert.DoesNotContain(IdentityProviderFactory.TestRsaPem()[..40], Describe(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_the_production_environment_while_only_uae_pass_staging_exists()
    {
        using var factory = new IdentityProviderFactory
        {
            Deployed = true,
            Environment = "Production",
            UaePassEnabled = true,
            SigningKeyPem = IdentityProviderFactory.TestRsaPem(),
        };

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Production", Describe(exception), StringComparison.Ordinal);
        Assert.Contains("stg-id.uaepass.ae", Describe(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_a_bad_signing_key_in_development()
    {
        using var factory = new IdentityProviderFactory { SigningKeyPem = "not a key" };

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("IdentityProvider:SigningKey:Pem", Describe(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_uae_pass_enabled_without_credentials_starts_with_uae_pass_off()
    {
        await using var factory = new IdentityProviderFactory { UaePassEnabled = true }.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentityProvider:UaePass:ClientId"] = "",
                    ["IdentityProvider:UaePass:ClientSecret"] = "",
                })));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(IdentityProviderFactory.Issuer), AllowAutoRedirect = false });

        using var discovery = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["idp_hint"] = "uae_pass";
        using var authorize = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);
        Assert.Equal("temporarily_unavailable", AuthorizationResponse.From(authorize.Headers.Location!)["error"]);
    }

    [Fact]
    public async Task It_starts_in_development()
    {
        await using var factory = new IdentityProviderFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://localhost:5090")]
    [InlineData("http://localhost:5090/?tenant=1")]
    public void It_refuses_an_issuer_that_is_not_a_plain_http_url(string issuer)
    {
        using var factory = new IdentityProviderFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["IdentityProvider:Issuer"] = issuer })));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("IdentityProvider:Issuer", Describe(exception), StringComparison.Ordinal);
    }

    private static Exception Innermost(Exception exception) =>
        exception.InnerException is null ? exception : Innermost(exception.InnerException);

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";
}
