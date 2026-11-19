using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderClientAuthenticationTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private static async Task<(AuthorizationResponse Redirect, Pkce Pkce)> AdminCodeAsync(HttpClient client)
    {
        var pkce = Pkce.Create();
        var parameters = IdentityProviderFlow.AuthorizeParameters(IdentityProviderFlow.AdminClientId, IdentityProviderFlow.AdminRedirectUri, pkce);
        var redirect = await IdentityProviderFlow.ChooseAsync(client, parameters, "researcher", TestContext.Current.CancellationToken);
        return (redirect, pkce);
    }

    private static async Task<HttpResponseMessage> AdminExchangeAsync(HttpClient client, ClientCredentials credentials)
    {
        var (redirect, pkce) = await AdminCodeAsync(client);
        return await IdentityProviderFlow.ExchangeCodeAsync(
            client, credentials, redirect.Require("code"), IdentityProviderFlow.AdminRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertInvalidClientAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_client", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_admin_client_authenticates_with_client_secret_basic()
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(client, ClientCredentials.AdminBasic());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_client_authenticates_with_client_secret_post()
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(client, ClientCredentials.AdminPost());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_client_without_its_secret_is_an_invalid_client()
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(
            client, new ClientCredentials(IdentityProviderFlow.AdminClientId, null, ClientAuthentication.None));

        await AssertInvalidClientAsync(response);
    }

    [Theory]
    [InlineData(ClientAuthentication.Basic)]
    [InlineData(ClientAuthentication.Post)]
    public async Task The_admin_client_with_a_wrong_secret_is_an_invalid_client(ClientAuthentication method)
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(
            client, new ClientCredentials(IdentityProviderFlow.AdminClientId, $"{IdentityProviderFactory.AdminClientSecret}-wrong", method));

        await AssertInvalidClientAsync(response);
    }

    [Fact]
    public async Task A_failed_basic_authentication_asks_for_basic_credentials()
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(client, ClientCredentials.AdminBasic("wrong"));

        await AssertInvalidClientAsync(response);
        Assert.Equal("Basic", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData(ClientAuthentication.Basic)]
    [InlineData(ClientAuthentication.Post)]
    public async Task The_admin_client_is_refused_while_no_secret_is_configured(ClientAuthentication method)
    {
        await using var provider = new IdentityProviderFactory { ConfiguredAdminClientSecret = "" };
        using var client = provider.CreateClient();

        using var withEmpty = await AdminExchangeAsync(client, new ClientCredentials(IdentityProviderFlow.AdminClientId, "", method));
        using var withGuess = await AdminExchangeAsync(client, new ClientCredentials(IdentityProviderFlow.AdminClientId, "change-me-dev-only", method));

        await AssertInvalidClientAsync(withEmpty);
        await AssertInvalidClientAsync(withGuess);
    }

    [Fact]
    public async Task An_unknown_client_is_an_invalid_client()
    {
        using var client = factory.CreateClient();

        using var response = await AdminExchangeAsync(client, new ClientCredentials("someone-else", "secret", ClientAuthentication.Post));

        await AssertInvalidClientAsync(response);
    }

    [Fact]
    public async Task A_request_without_any_client_is_an_invalid_client()
    {
        using var client = factory.CreateClient();
        using var body = new FormUrlEncodedContent([new("grant_type", "refresh_token"), new("refresh_token", "anything")]);

        using var response = await client.PostAsync("/token", body, TestContext.Current.CancellationToken);

        await AssertInvalidClientAsync(response);
    }

    [Fact]
    public async Task A_public_client_that_sends_a_secret_is_refused()
    {
        using var client = factory.CreateClient();
        var pkce = Pkce.Create();
        var redirect = await IdentityProviderFlow.ChooseAsync(
            client, IdentityProviderFlow.MobileParameters(pkce), "researcher", TestContext.Current.CancellationToken);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, new ClientCredentials(IdentityProviderFlow.MobileClientId, "a-secret", ClientAuthentication.Post),
            redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier, TestContext.Current.CancellationToken);

        await AssertInvalidClientAsync(response);
    }

    [Fact]
    public async Task Two_authentication_methods_in_one_request_are_an_invalid_request()
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await AdminCodeAsync(client);
        using var request = IdentityProviderFlow.ClientRequest("/token", ClientCredentials.AdminPost(),
            ("grant_type", "authorization_code"), ("code", redirect.Require("code")),
            ("redirect_uri", IdentityProviderFlow.AdminRedirectUri), ("code_verifier", pkce.Verifier));
        var pair = $"{IdentityProviderFlow.AdminClientId}:{IdentityProviderFactory.AdminClientSecret}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }
}
