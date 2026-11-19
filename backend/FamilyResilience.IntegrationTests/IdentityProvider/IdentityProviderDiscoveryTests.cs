using System.Net;
using System.Text.Json;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderDiscoveryTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private static readonly string[] PrivateKeyMembers = ["d", "p", "q", "dp", "dq", "qi"];

    private async Task<JsonElement> DiscoveryAsync(string baseAddress)
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(baseAddress), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
    }

    private static string[] Strings(JsonElement metadata, string name) =>
        metadata.GetProperty(name).EnumerateArray().Select(value => value.GetString()!).ToArray();

    [Fact]
    public async Task Discovery_names_the_issuer_its_endpoints_and_that_it_is_for_development_only()
    {
        var metadata = await DiscoveryAsync(IdentityProviderFactory.Issuer);

        Assert.Equal(IdentityProviderFactory.Issuer, metadata.GetProperty("issuer").GetString());
        Assert.True(metadata.GetProperty("azwah_development_only").GetBoolean());
        Assert.True(metadata.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.Equal("http://localhost:5090/authorize", metadata.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost:5090/token", metadata.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost:5090/jwks", metadata.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://localhost:5090/revoke", metadata.GetProperty("revocation_endpoint").GetString());
        Assert.Equal("http://localhost:5090/end-session", metadata.GetProperty("end_session_endpoint").GetString());
        Assert.Equal(new[] { "code" }, Strings(metadata, "response_types_supported"));
        Assert.Equal(new[] { "S256" }, Strings(metadata, "code_challenge_methods_supported"));
        Assert.Equal(new[] { "RS256" }, Strings(metadata, "id_token_signing_alg_values_supported"));
        Assert.Equal(new[] { "authorization_code", "refresh_token" }, Strings(metadata, "grant_types_supported"));
        Assert.Equal(
            new[] { "client_secret_basic", "client_secret_post", "none" },
            Strings(metadata, "token_endpoint_auth_methods_supported"));
        Assert.Contains("openid", Strings(metadata, "scopes_supported"));
    }

    [Fact]
    public async Task Discovery_builds_endpoint_urls_from_the_request_but_never_changes_the_issuer()
    {
        // The API in Docker Compose fetches keys from http://identity-provider:5090 while browsers use localhost.
        var metadata = await DiscoveryAsync("http://identity-provider:5090");

        Assert.Equal(IdentityProviderFactory.Issuer, metadata.GetProperty("issuer").GetString());
        Assert.Equal("http://identity-provider:5090/jwks", metadata.GetProperty("jwks_uri").GetString());
        Assert.Equal("http://identity-provider:5090/token", metadata.GetProperty("token_endpoint").GetString());
    }

    [Fact]
    public async Task Jwks_publishes_one_public_rs256_signing_key()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/jwks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwks = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        var key = Assert.Single(jwks.GetProperty("keys").EnumerateArray().ToList());
        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrWhiteSpace(key.GetProperty("kid").GetString()));
        Assert.Equal("AQAB", key.GetProperty("e").GetString());
        // A 2048-bit modulus is 256 bytes: 342 base64url characters without padding.
        Assert.Equal(342, key.GetProperty("n").GetString()!.Length);
        Assert.DoesNotContain(TestJwt.Names(key), name => PrivateKeyMembers.Contains(name));
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/jwks")]
    public async Task Json_responses_are_not_cached_and_carry_the_security_headers(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            Single(response, "Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Server"));
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.GetValues(header));
}
