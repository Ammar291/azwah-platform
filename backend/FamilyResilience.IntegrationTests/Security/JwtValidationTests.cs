using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// The API's own JwtBearer handler, with the production validation rules: the test scheme is switched off, so every
/// request here carries a real signed token or none.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JwtValidationTests(ApiFactory factory)
{
    private const string RejectionCategory = "FamilyResilience.Api.Authorization.TokenValidation";

    private static async Task<HttpResponseMessage> GetMeAsync(WebApplicationFactory<Program> host, string? token)
    {
        using var client = host.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
    }

    private async Task AssertRejectedAsync(string token, string reason)
    {
        var logs = new CapturingLoggerProvider();
        await using var host = factory.BearerHost(logs);

        using var response = await GetMeAsync(host, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        var rejection = Assert.Single(logs.Entries, entry => entry.Category == RejectionCategory);
        Assert.Equal($"Rejected a bearer token: {reason}", rejection.Message);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(token, StringComparison.Ordinal));
        if (token.Split('.') is [_, { Length: > 16 } payload, ..])
        {
            Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(payload, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_valid_token_signs_the_caller_in_with_the_roles_it_carries()
    {
        await using var host = factory.BearerHost();
        var subject = ApiClients.NewSubject("jwt");

        using var response = await GetMeAsync(host, BearerTokens.Create(subject, ["researcher", "program_administrator"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal(subject, me!.Subject);
        Assert.Equal(["program_administrator", "researcher"], me.Roles);
    }

    [Fact]
    public async Task A_request_without_a_token_gets_a_bare_bearer_challenge()
    {
        await using var host = factory.BearerHost();

        using var response = await GetMeAsync(host, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
    }

    [Fact]
    public Task A_token_signed_with_another_key_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], key: BearerTokens.UntrustedKey), "signature");

    [Fact]
    public Task A_token_from_another_issuer_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], issuer: "https://other-issuer.test"), "issuer");

    [Fact]
    public Task A_token_for_another_audience_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], audience: "another-api"), "audience");

    [Fact]
    public Task An_unsigned_token_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Unsigned("jwt-subject"), "signature");

    /// <summary>IdentityModel reports an algorithm outside the allowed list as a signature failure.</summary>
    [Fact]
    public Task An_hs256_token_is_rejected_outside_development_even_with_a_trusted_key() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], key: BearerTokens.SymmetricKey), "signature");

    [Fact]
    public Task A_malformed_token_is_rejected() =>
        AssertRejectedAsync("this-is-not-a-json-web-token", "malformed");

    [Fact]
    public Task An_expired_token_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], expires: DateTimeOffset.UtcNow.AddMinutes(-2)), "expired");

    [Fact]
    public Task A_token_without_an_expiry_is_rejected() =>
        AssertRejectedAsync(BearerTokens.Create("jwt-subject", ["researcher"], withExpiry: false), "lifetime");

    [Fact]
    public async Task A_token_that_expired_within_the_clock_skew_is_still_accepted()
    {
        await using var host = factory.BearerHost();

        using var response = await GetMeAsync(
            host, BearerTokens.Create(ApiClients.NewSubject("jwt"), ["researcher"], expires: DateTimeOffset.UtcNow.AddSeconds(-30)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_stays_anonymous_with_a_broken_token(string path)
    {
        await using var host = factory.BearerHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record MeBody(string Subject, string[] Roles);
}
