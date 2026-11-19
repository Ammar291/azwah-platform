using System.Net;
using System.Text.Json;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderTokenTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private static readonly string[] PersonalClaims =
        ["name", "given_name", "family_name", "email", "phone_number", "birthdate", "address", "preferred_username"];

    private async Task<JsonElement> SigningKeyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/jwks", TestContext.Current.CancellationToken);
        var jwks = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        return Assert.Single(jwks.GetProperty("keys").EnumerateArray().ToList());
    }

    private async Task<(AuthorizationResponse Redirect, Pkce Pkce)> CodeAsync(
        HttpClient client, string clientId, string redirectUri, string persona = "researcher")
    {
        var pkce = Pkce.Create();
        var parameters = IdentityProviderFlow.AuthorizeParameters(clientId, redirectUri, pkce);
        var redirect = await IdentityProviderFlow.ChooseAsync(client, parameters, persona, TestContext.Current.CancellationToken);
        return (redirect, pkce);
    }

    [Fact]
    public async Task The_code_flow_issues_bearer_access_id_and_refresh_tokens_that_are_never_cached()
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-cache", Assert.Single(response.Headers.GetValues("Pragma")));
        var tokens = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.Equal(600, tokens.GetProperty("expires_in").GetInt32());
        Assert.Equal("openid offline_access", tokens.GetProperty("scope").GetString());
        Assert.Equal(3, tokens.GetProperty("access_token").GetString()!.Split('.').Length);
        Assert.Equal(3, tokens.GetProperty("id_token").GetString()!.Split('.').Length);
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("refresh_token").GetString()));
    }

    [Fact]
    public async Task The_access_token_is_a_signed_at_jwt_for_the_api_with_the_personas_roles_and_nothing_personal()
    {
        using var client = factory.CreateClient();
        var key = await SigningKeyAsync(client);
        var before = factory.Clock.GetUtcNow().ToUnixTimeSeconds();

        var signedIn = await IdentityProviderFlow.SignInAsync(client, "participant_lead", TestContext.Current.CancellationToken);

        var header = TestJwt.Header(signedIn.AccessToken);
        Assert.Equal("at+jwt", header.GetProperty("typ").GetString());
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal(key.GetProperty("kid").GetString(), header.GetProperty("kid").GetString());
        Assert.True(TestJwt.HasValidRs256Signature(signedIn.AccessToken, key));

        var claims = TestJwt.Payload(signedIn.AccessToken);
        Assert.Equal(
            new[] { "aud", "azwah_development_only", "client_id", "exp", "iat", "iss", "jti", "nbf", "role", "scope", "sub" },
            TestJwt.Names(claims).Order(StringComparer.Ordinal));
        Assert.Equal(IdentityProviderFactory.Issuer, claims.GetProperty("iss").GetString());
        Assert.Equal("dev-participant-lead", claims.GetProperty("sub").GetString());
        Assert.Equal("azwah-api", claims.GetProperty("aud").GetString());
        Assert.Equal(IdentityProviderFlow.MobileClientId, claims.GetProperty("client_id").GetString());
        Assert.Equal("openid offline_access", claims.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Array, claims.GetProperty("role").ValueKind);
        Assert.Equal(new[] { "participant" }, claims.GetProperty("role").EnumerateArray().Select(role => role.GetString()!));
        Assert.Equal(before, claims.GetProperty("iat").GetInt64());
        Assert.Equal(before, claims.GetProperty("nbf").GetInt64());
        Assert.Equal(before + 600, claims.GetProperty("exp").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(claims.GetProperty("jti").GetString()));
        Assert.DoesNotContain(TestJwt.Names(claims), name => PersonalClaims.Contains(name));
    }

    [Fact]
    public async Task Every_access_token_has_its_own_jti()
    {
        using var client = factory.CreateClient();

        var first = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        var second = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);

        Assert.NotEqual(
            TestJwt.Payload(first.AccessToken).GetProperty("jti").GetString(),
            TestJwt.Payload(second.AccessToken).GetProperty("jti").GetString());
    }

    [Fact]
    public async Task The_id_token_carries_the_nonce_and_names_the_client_as_audience_and_authorized_party()
    {
        using var client = factory.CreateClient();
        var key = await SigningKeyAsync(client);
        var before = factory.Clock.GetUtcNow().ToUnixTimeSeconds();

        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "program_administrator", TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        var header = TestJwt.Header(signedIn.IdToken);
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal(key.GetProperty("kid").GetString(), header.GetProperty("kid").GetString());
        Assert.True(TestJwt.HasValidRs256Signature(signedIn.IdToken, key));

        var claims = TestJwt.Payload(signedIn.IdToken);
        Assert.Equal(
            new[] { "aud", "auth_time", "azp", "azwah_development_only", "exp", "iat", "iss", "nonce", "sub" },
            TestJwt.Names(claims).Order(StringComparer.Ordinal));
        Assert.Equal(IdentityProviderFactory.Issuer, claims.GetProperty("iss").GetString());
        Assert.Equal("dev-program-administrator", claims.GetProperty("sub").GetString());
        Assert.Equal(IdentityProviderFlow.AdminClientId, claims.GetProperty("aud").GetString());
        Assert.Equal(IdentityProviderFlow.AdminClientId, claims.GetProperty("azp").GetString());
        Assert.Equal(signedIn.Nonce, claims.GetProperty("nonce").GetString());
        Assert.Equal(before, claims.GetProperty("auth_time").GetInt64());
        Assert.Equal(before, claims.GetProperty("iat").GetInt64());
        Assert.True(claims.GetProperty("exp").GetInt64() > before);
    }

    [Fact]
    public async Task A_wrong_code_verifier_is_an_invalid_grant()
    {
        using var client = factory.CreateClient();
        var (redirect, _) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, Pkce.Create().Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_code_works_once_and_its_reuse_revokes_the_refresh_token_it_issued()
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);
        var code = redirect.Require("code");
        using var first = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, code, IdentityProviderFlow.MobileRedirectUri, pkce.Verifier, TestContext.Current.CancellationToken);
        var refreshToken = (await IdentityProviderFlow.ReadJsonAsync(first, TestContext.Current.CancellationToken))
            .GetProperty("refresh_token").GetString()!;

        using var reused = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, code, IdentityProviderFlow.MobileRedirectUri, pkce.Verifier, TestContext.Current.CancellationToken);
        using var refreshed = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, refreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(reused, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(refreshed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_code_issued_to_another_client_is_an_invalid_grant()
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.AdminBasic(), redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Another_redirect_uri_is_an_invalid_grant_even_when_it_is_registered()
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.ExpoGoRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_code_is_an_invalid_grant()
    {
        using var client = factory.CreateClient();

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, IdentityProviderFlow.RandomValue(), IdentityProviderFlow.MobileRedirectUri, Pkce.Create().Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_code_expires_after_sixty_seconds()
    {
        await using var provider = new IdentityProviderFactory();
        using var client = provider.CreateClient();
        var pkce = Pkce.Create();
        var redirect = await IdentityProviderFlow.ChooseAsync(
            client, IdentityProviderFlow.MobileParameters(pkce), "researcher", TestContext.Current.CancellationToken);
        provider.Clock.Advance(TimeSpan.FromSeconds(61));

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("redirect_uri")]
    [InlineData("code_verifier")]
    public async Task A_code_exchange_without_a_required_field_is_an_invalid_request(string missing)
    {
        using var client = factory.CreateClient();
        var (redirect, pkce) = await CodeAsync(client, IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri);
        var fields = new List<(string Key, string Value)>
        {
            ("grant_type", "authorization_code"),
            ("code", redirect.Require("code")),
            ("redirect_uri", IdentityProviderFlow.MobileRedirectUri),
            ("code_verifier", pkce.Verifier),
        };
        fields.RemoveAll(field => field.Key == missing);
        using var request = IdentityProviderFlow.ClientRequest("/token", ClientCredentials.Mobile, [.. fields]);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("client_credentials")]
    [InlineData("urn:ietf:params:oauth:grant-type:device_code")]
    public async Task Other_grant_types_are_unsupported(string grantType)
    {
        using var client = factory.CreateClient();
        using var request = IdentityProviderFlow.ClientRequest("/token", ClientCredentials.Mobile, ("grant_type", grantType));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unsupported_grant_type", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_token_request_that_is_not_a_form_is_an_invalid_request()
    {
        using var client = factory.CreateClient();
        using var body = new StringContent("""{"grant_type":"authorization_code"}""", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/token", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_offline_access_no_refresh_token_is_issued()
    {
        using var client = factory.CreateClient();
        var pkce = Pkce.Create();
        var parameters = IdentityProviderFlow.MobileParameters(pkce);
        parameters["scope"] = "openid";
        var redirect = await IdentityProviderFlow.ChooseAsync(client, parameters, "researcher", TestContext.Current.CancellationToken);

        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);

        var tokens = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("openid", tokens.GetProperty("scope").GetString());
        Assert.False(tokens.TryGetProperty("refresh_token", out _));
    }
}
