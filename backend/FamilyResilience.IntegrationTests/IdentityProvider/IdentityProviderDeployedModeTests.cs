using System.Net;
using System.Text.Json;
using FamilyResilience.IdentityProvider;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// Outside Development the provider offers UAE PASS only: no persona page or choice, no development markers in
/// discovery, tokens or pages, and discovery endpoints built from the issuer rather than the request (D-129).
/// </summary>
public sealed class IdentityProviderDeployedModeTests
{
    private static async Task<JsonElement> DiscoveryAsync(HttpClient client, string? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Authorize_goes_straight_to_uae_pass_without_a_hint()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var client = provider.CreateClient();

        using var response = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var upstream = AuthorizationResponse.From(response.Headers.Location!);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/authorize", upstream.Target);
        Assert.Equal("https://id.azwah.test/uaepass/callback", upstream["redirect_uri"]);
    }

    [Fact]
    public async Task A_persona_choice_is_refused()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        using var response = await client.PostAsync("/authorize", IdentityProviderFlow.Form(parameters, ("persona", "researcher")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var redirect = AuthorizationResponse.From(response.Headers.Location!);
        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("invalid_request", redirect["error"]);
        Assert.Null(redirect["code"]);
    }

    [Fact]
    public async Task Discovery_and_tokens_carry_no_development_marker()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var client = provider.CreateClient();

        var metadata = await DiscoveryAsync(client);
        var tokens = await IdentityProviderFlow.SignInWithUaePassAsync(client, TestContext.Current.CancellationToken);

        Assert.False(metadata.TryGetProperty("azwah_development_only", out _));
        var access = TestJwt.Payload(tokens.GetProperty("access_token").GetString()!);
        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.False(access.TryGetProperty("azwah_development_only", out _));
        Assert.False(id.TryGetProperty("azwah_development_only", out _));
        Assert.Equal("uaepass-stg|11111111-2222-3333-4444-555555555555", access.GetProperty("sub").GetString());
        Assert.Equal(IdentityProviderFactory.DeployedIssuer, access.GetProperty("iss").GetString());
        Assert.Equal("uae_pass_staging", id.GetProperty("azwah_profile").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Discovery_endpoints_come_from_the_issuer()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var client = provider.CreateClient();

        var metadata = await DiscoveryAsync(client, "internal:5090");

        Assert.Equal(IdentityProviderFactory.DeployedIssuer, metadata.GetProperty("issuer").GetString());
        Assert.Equal("https://id.azwah.test/authorize", metadata.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("https://id.azwah.test/jwks", metadata.GetProperty("jwks_uri").GetString());
        Assert.Equal("https://id.azwah.test/end-session", metadata.GetProperty("end_session_endpoint").GetString());
    }

    [Fact]
    public async Task Pages_carry_no_simulated_banner_and_list_no_personas()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var client = provider.CreateClient();

        using var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        using var refused = await client.GetAsync("/authorize?client_id=unknown", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var homeBody = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Pages.Label, homeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("dev-participant-lead", homeBody, StringComparison.Ordinal);
        Assert.Contains("UAE PASS", homeBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var refusedBody = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Pages.Label, refusedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("development only", refusedBody, StringComparison.Ordinal);
    }
}
