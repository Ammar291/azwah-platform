using System.Net;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IntegrationTests.Security;

[Collection(ApiCollection.Name)]
public sealed class SecurityHeadersTests(ApiFactory factory)
{
    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        // The exception handler and health checks add no-cache to no-store; no-store is what matters.
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task Success_responses_carry_the_security_headers_and_no_store()
    {
        using var client = factory.AsParticipant();

        using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Theory]
    [InlineData(null, "/api/v1/me", HttpStatusCode.Unauthorized)]
    [InlineData("participant", "/api/v1/households/0199f1a2-0000-7000-8000-000000000001", HttpStatusCode.NotFound)]
    [InlineData("participant", "/test-only/throw", HttpStatusCode.InternalServerError)]
    public async Task Problem_responses_carry_them_too(string? role, string path, HttpStatusCode status)
    {
        using var client = role is null ? factory.As(null) : factory.As(ApiClients.NewSubject("headers"), role);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Health_responses_are_not_cached_either()
    {
        using var client = factory.As(null);

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public void Kestrel_limits_bodies_headers_and_request_lines_and_sends_no_server_header()
    {
        var limits = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        Assert.Equal(1_048_576, limits.MaxRequestBodySize);
        Assert.Equal(32_768, limits.MaxRequestHeadersTotalSize);
        Assert.Equal(8_192, limits.MaxRequestLineSize);
        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }
}
