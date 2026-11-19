using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Security;

[Collection(ApiCollection.Name)]
public sealed class RateLimitingTests(ApiFactory factory)
{
    private WebApplicationFactory<Program> LimitedHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                [$"RateLimiting:Policies:{RateLimitPolicies.ParticipantWrites}:PermitLimit"] = "2",
                [$"RateLimiting:Policies:{RateLimitPolicies.ParticipantWrites}:WindowSeconds"] = "600",
                ["RateLimiting:Global:AnonymousPermitLimit"] = "3",
                ["RateLimiting:Global:WindowSeconds"] = "600",
            })));

    private static HttpClient Participant(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "participant");
        return client;
    }

    private static Task<HttpResponseMessage> CreateEmptyHouseholdAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/v1/households", new { }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_third_write_in_a_window_gets_429_with_retry_after_and_a_problem_code()
    {
        await using var host = LimitedHost();
        using var client = Participant(host, ApiClients.NewSubject("limited"));

        using var first = await CreateEmptyHouseholdAsync(client);
        using var second = await CreateEmptyHouseholdAsync(client);
        using var third = await CreateEmptyHouseholdAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", third.Content.Headers.ContentType?.MediaType);
        var retryAfter = Assert.Single(third.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 600);
        using var json = JsonDocument.Parse(await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("requests.rate_limited", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Another_subject_has_its_own_window()
    {
        await using var host = LimitedHost();
        using var busy = Participant(host, ApiClients.NewSubject("busy"));
        using var other = Participant(host, ApiClients.NewSubject("other"));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var _ = await CreateEmptyHouseholdAsync(busy);
        }

        using var response = await CreateEmptyHouseholdAsync(other);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reads_are_not_counted_against_a_write_policy()
    {
        await using var host = LimitedHost();
        using var client = Participant(host, ApiClients.NewSubject("reader"));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var read = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using var write = await CreateEmptyHouseholdAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_limited_by_address()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            statuses);
    }

    [Fact]
    public async Task Anonymous_requests_to_a_policy_endpoint_count_only_against_the_global_limit()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await CreateEmptyHouseholdAsync(client);
            statuses.Add(response.StatusCode);
        }

        // participant-writes allows 2 here; anonymous callers are refused by authorization and never get a partition.
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized], statuses);
    }

    [Fact]
    public async Task Health_is_never_rate_limited()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    public static TheoryData<string, string, string> SensitiveEndpoints => new()
    {
        { "POST", "api/v1/households", RateLimitPolicies.ParticipantWrites },
        { "PATCH", "api/v1/households/{householdId:guid}", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/households/{householdId:guid}/members", RateLimitPolicies.ParticipantWrites },
        { "PATCH", "api/v1/households/{householdId:guid}/members/{memberId:guid}", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/households/{householdId:guid}/confirm-roster", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/consent", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/assignments/{assignmentId:guid}/sessions", RateLimitPolicies.ParticipantWrites },
        { "PUT", "api/v1/sessions/{sessionId:guid}/responses", RateLimitPolicies.Answers },
        { "POST", "api/v1/sessions/{sessionId:guid}/submit", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/me/registration", RateLimitPolicies.ParticipantWrites },
        { "POST", "api/v1/me/sign-ins", RateLimitPolicies.Identity },
        { "POST", "api/v1/me/sign-outs", RateLimitPolicies.Identity },
        { "GET", "api/v1/me/data-export", RateLimitPolicies.DataExports },
        { "POST", "api/v1/me/privacy-requests", RateLimitPolicies.ParticipantWrites },
        { "GET", "api/v1/admin/privacy-requests", RateLimitPolicies.StaffReads },
        { "POST", "api/v1/admin/privacy-requests/{requestId:guid}/close", RateLimitPolicies.StaffWrites },
        { "GET", "api/v1/admin/safety-alerts", RateLimitPolicies.StaffReads },
        { "GET", "api/v1/admin/safety-alerts/summary", RateLimitPolicies.StaffReads },
        { "POST", "api/v1/admin/safety-alerts/{alertId:guid}/acknowledge", RateLimitPolicies.StaffWrites },
        { "POST", "api/v1/admin/safety-alerts/{alertId:guid}/resolve", RateLimitPolicies.StaffWrites },
        { "GET", "api/v1/admin/analytics/overview", RateLimitPolicies.StaffReads },
        { "GET", "api/v1/admin/analytics/score-distributions", RateLimitPolicies.StaffReads },
        { "GET", "api/v1/admin/participation", RateLimitPolicies.StaffReads },
        { "GET", "api/v1/admin/assessments", RateLimitPolicies.StaffReads },
        { "GET", "api/v1/admin/referrals", RateLimitPolicies.StaffReads },
        { "POST", "api/v1/admin/exports", RateLimitPolicies.Exports },
        { "GET", "api/v1/admin/exports/{exportId:guid}/file", RateLimitPolicies.ExportDownloads },
        { "POST", "api/v1/support-resources/search", RateLimitPolicies.SupportSearches },
        { "POST", "api/v1/assistant/replies", RateLimitPolicies.Assistant },
        { "POST", "api/v1/sessions/{sessionId:guid}/report", RateLimitPolicies.ScoreReports },
        { "POST", "api/v1/sessions/{sessionId:guid}/report/parts/{sectionCode}", RateLimitPolicies.ScoreReportParts },
        { "POST", "api/v1/sessions/{sessionId:guid}/report/summary", RateLimitPolicies.ScoreReportParts },
        { "GET", "api/v1/consultant/live", RateLimitPolicies.Consultant },
    };

    [Theory]
    [MemberData(nameof(SensitiveEndpoints))]
    public void Sensitive_endpoints_carry_their_policy(string method, string route, string policy)
    {
        var endpoint = Assert.Single(Endpoints(), endpoint => endpoint.Method == method && endpoint.Route == route);

        Assert.Equal(policy, endpoint.Policy);
    }

    [Fact]
    public void Every_state_changing_endpoint_has_a_named_policy()
    {
        var unlimited = Endpoints()
            .Where(endpoint => endpoint.Method is "POST" or "PUT" or "PATCH" or "DELETE" && endpoint.Route.StartsWith("api/", StringComparison.Ordinal))
            .Where(endpoint => endpoint.Policy is null)
            .Select(endpoint => $"{endpoint.Method} {endpoint.Route}")
            .ToList();

        Assert.True(unlimited.Count == 0, $"Give these endpoints a rate-limit policy: {string.Join(", ", unlimited)}");
    }

    private List<(string Method, string Route, string? Policy)> Endpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (method, endpoint.RoutePattern.RawText ?? string.Empty,
                    endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName)))
            .ToList();
}
