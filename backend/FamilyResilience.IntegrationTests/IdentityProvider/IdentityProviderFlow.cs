using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>Drives the development identity provider the way an OpenID Connect client does.</summary>
public static class IdentityProviderFlow
{
    public const string MobileClientId = "azwah-mobile";
    public const string MobileRedirectUri = "azwah://auth/callback";
    public const string ExpoGoRedirectUri = "exp://192.168.1.20:8081/--/auth/callback";
    public const string AdminClientId = "azwah-admin";
    public const string AdminRedirectUri = "http://localhost:3000/auth/callback";

    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(16));

    /// <summary>A valid authorization request; tests change or remove entries (null removes).</summary>
    public static Dictionary<string, string?> AuthorizeParameters(string clientId, string redirectUri, Pkce pkce) => new()
    {
        ["client_id"] = clientId,
        ["redirect_uri"] = redirectUri,
        ["response_type"] = "code",
        ["scope"] = "openid offline_access",
        ["state"] = RandomValue(),
        ["nonce"] = RandomValue(),
        ["code_challenge"] = pkce.Challenge,
        ["code_challenge_method"] = "S256",
    };

    public static Dictionary<string, string?> MobileParameters(Pkce pkce) =>
        AuthorizeParameters(MobileClientId, MobileRedirectUri, pkce);

    public static string AuthorizeUrl(IDictionary<string, string?> parameters) =>
        QueryHelpers.AddQueryString("/authorize", Present(parameters));

    public static FormUrlEncodedContent Form(IDictionary<string, string?> parameters, params (string Key, string Value)[] extra) =>
        new(Present(parameters).Concat(extra.Select(field => new KeyValuePair<string, string?>(field.Key, field.Value))));

    /// <summary>Opens the persona page, chooses <paramref name="persona"/>, and returns where the provider redirected.</summary>
    public static async Task<AuthorizationResponse> ChooseAsync(
        HttpClient client, IDictionary<string, string?> parameters, string persona, CancellationToken cancellationToken)
    {
        using (var page = await client.GetAsync(AuthorizeUrl(parameters), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        }

        using var chosen = await client.PostAsync("/authorize", Form(parameters, ("persona", persona)), cancellationToken);
        Assert.Equal(HttpStatusCode.Found, chosen.StatusCode);
        return AuthorizationResponse.From(chosen.Headers.Location!);
    }

    /// <summary>A token endpoint (or revocation endpoint) request authenticated as <paramref name="client"/>.</summary>
    public static HttpRequestMessage ClientRequest(string path, ClientCredentials client, params (string Key, string Value)[] fields)
    {
        var form = fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)).ToList();
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        switch (client.Method)
        {
            case ClientAuthentication.None:
                form.Add(new("client_id", client.ClientId));
                break;
            case ClientAuthentication.Post:
                form.Add(new("client_id", client.ClientId));
                form.Add(new("client_secret", client.Secret!));
                break;
            case ClientAuthentication.Basic:
                // RFC 6749 section 2.3.1: form-encode both values, then base64 "id:secret".
                var pair = $"{Uri.EscapeDataString(client.ClientId)}:{Uri.EscapeDataString(client.Secret!)}";
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
                break;
        }

        request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    public static async Task<HttpResponseMessage> ExchangeCodeAsync(
        HttpClient client, ClientCredentials credentials, string code, string redirectUri, string verifier, CancellationToken cancellationToken)
    {
        using var request = ClientRequest("/token", credentials,
            ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", redirectUri), ("code_verifier", verifier));
        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<HttpResponseMessage> RefreshAsync(
        HttpClient client, ClientCredentials credentials, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = ClientRequest("/token", credentials, ("grant_type", "refresh_token"), ("refresh_token", refreshToken));
        return await client.SendAsync(request, cancellationToken);
    }

    public static async Task<HttpResponseMessage> RevokeAsync(
        HttpClient client, ClientCredentials credentials, string token, CancellationToken cancellationToken)
    {
        using var request = ClientRequest("/revoke", credentials, ("token", token), ("token_type_hint", "refresh_token"));
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>The whole authorization code flow with PKCE for <paramref name="persona"/>.</summary>
    public static async Task<SignedIn> SignInAsync(
        HttpClient client, string persona, CancellationToken cancellationToken, ClientCredentials? credentials = null)
    {
        credentials ??= ClientCredentials.Mobile;
        var redirectUri = credentials.ClientId == AdminClientId ? AdminRedirectUri : MobileRedirectUri;
        var pkce = Pkce.Create();
        var parameters = AuthorizeParameters(credentials.ClientId, redirectUri, pkce);
        var authorization = await ChooseAsync(client, parameters, persona, cancellationToken);
        using var response = await ExchangeCodeAsync(
            client, credentials, authorization.Require("code"), redirectUri, pkce.Verifier, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await ReadJsonAsync(response, cancellationToken);
        return new SignedIn(parameters["nonce"]!, tokens);
    }

    /// <summary>
    /// A whole UAE PASS sign-in through the <see cref="FakeUaePass"/> stub, redeemed by the mobile client: the token
    /// response (access, ID and refresh tokens). Works in both modes; the hint is only needed in Development.
    /// </summary>
    public static async Task<JsonElement> SignInWithUaePassAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var pkce = Pkce.Create();
        var parameters = MobileParameters(pkce);
        parameters["idp_hint"] = "uae_pass";
        using var start = await client.GetAsync(AuthorizeUrl(parameters), cancellationToken);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        var upstream = AuthorizationResponse.From(start.Headers.Location!);
        using var callback = await client.GetAsync(
            QueryHelpers.AddQueryString("/uaepass/callback", new Dictionary<string, string?> { ["code"] = "stub-upstream-code", ["state"] = upstream.Require("state") }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var redirect = AuthorizationResponse.From(callback.Headers.Location!);
        using var exchange = await ExchangeCodeAsync(client, ClientCredentials.Mobile, redirect.Require("code"), MobileRedirectUri, pkce.Verifier, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        return await ReadJsonAsync(exchange, cancellationToken);
    }

    /// <summary>
    /// A whole Google sign-in through the <see cref="FakeGoogle"/> stub, redeemed by the mobile client (or
    /// <paramref name="credentials"/>): the token response (access, ID and refresh tokens). Works in both modes.
    /// </summary>
    public static async Task<JsonElement> SignInWithGoogleAsync(
        HttpClient client, CancellationToken cancellationToken, ClientCredentials? credentials = null)
    {
        credentials ??= ClientCredentials.Mobile;
        var redirectUri = credentials.ClientId == AdminClientId ? AdminRedirectUri : MobileRedirectUri;
        var pkce = Pkce.Create();
        var parameters = AuthorizeParameters(credentials.ClientId, redirectUri, pkce);
        parameters["idp_hint"] = "google";
        using var start = await client.GetAsync(AuthorizeUrl(parameters), cancellationToken);
        Assert.Equal(HttpStatusCode.Found, start.StatusCode);
        var upstream = AuthorizationResponse.From(start.Headers.Location!);
        using var callback = await client.GetAsync(
            QueryHelpers.AddQueryString("/google/callback", new Dictionary<string, string?> { ["code"] = "stub-google-code", ["state"] = upstream.Require("state") }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var redirect = AuthorizationResponse.From(callback.Headers.Location!);
        Assert.Equal(redirectUri, redirect.Target);
        using var exchange = await ExchangeCodeAsync(client, credentials, redirect.Require("code"), redirectUri, pkce.Verifier, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        return await ReadJsonAsync(exchange, cancellationToken);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await ReadJsonAsync(response, cancellationToken);
        return body.GetProperty("error").GetString();
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IEnumerable<KeyValuePair<string, string?>> Present(IDictionary<string, string?> parameters) =>
        parameters.Where(parameter => parameter.Value is not null);
}
