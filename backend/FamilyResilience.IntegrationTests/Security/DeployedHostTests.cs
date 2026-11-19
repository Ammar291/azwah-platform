using System.Net;
using System.Text.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>The rules that apply only outside Development and Testing: the start-up guard and TLS.</summary>
public sealed class DeployedHostTests
{
    [Theory]
    [InlineData("Authentication:Schemes:Bearer:Authority", "http://issuer.azwah.test", "authority_not_https")]
    [InlineData("Authentication:Schemes:Bearer:ValidAudiences:0", null, "audience_missing")]
    [InlineData("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=d;Username=u;Password=p", "database_tls_not_required")]
    [InlineData("RateLimiting:Enabled", "false", "rate_limiting_disabled")]
    [InlineData("ReverseProxy:KnownNetworks:0", "0.0.0.0/0", "forwarded_headers_trust_all")]
    public void A_deployed_host_refuses_to_start_with_unsafe_settings(string key, string? value, string problem)
    {
        using var factory = new DeployedApiFactory(new Dictionary<string, string?> { [key] = value });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateHttpsClient());

        Assert.Contains(problem, AllMessages(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deployed_host_answers_plain_http_with_400_https_required()
    {
        using var factory = new DeployedApiFactory();
        using var client = factory.CreatePlainHttpClient();

        using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("requests.https_required", json.RootElement.GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task A_deployed_host_lets_https_requests_reach_authentication_and_sends_hsts()
    {
        using var factory = new DeployedApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Health_answers_over_plain_http_for_probes()
    {
        using var factory = new DeployedApiFactory();
        using var client = factory.CreatePlainHttpClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Forwarded_https_is_trusted_only_from_a_known_proxy()
    {
        using var factory = new DeployedApiFactory(new Dictionary<string, string?> { ["ReverseProxy:KnownProxies:0"] = "10.0.0.5" });
        _ = factory.Server;

        var fromProxy = await SendAsync(factory.Server, "10.0.0.5");
        var fromElsewhere = await SendAsync(factory.Server, "203.0.113.9");

        Assert.Equal(StatusCodes.Status401Unauthorized, fromProxy.Response.StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, fromElsewhere.Response.StatusCode);
    }

    [Fact]
    public void Forwarded_headers_are_not_trusted_without_a_proxy_list()
    {
        using var factory = new DeployedApiFactory(new Dictionary<string, string?> { ["FORWARDEDHEADERS_ENABLED"] = "true" });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateHttpsClient());

        Assert.Contains("forwarded_headers_trust_all", AllMessages(error), StringComparison.Ordinal);
    }

    private static Task<HttpContext> SendAsync(TestServer server, string remoteAddress) =>
        server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Scheme = "http";
            context.Request.Host = new HostString(DeployedApiFactory.PublicHost);
            context.Request.Path = "/api/v1/me";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
        }, TestContext.Current.CancellationToken);

    private static string AllMessages(Exception error)
    {
        var messages = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(" | ", messages);
    }
}
