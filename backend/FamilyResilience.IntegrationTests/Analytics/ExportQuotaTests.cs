using System.Net;
using System.Net.Http.Json;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>
/// Research exports are rationed per researcher: at most two queued or running, at most twenty a day. Background jobs are
/// off in the test host, so every request here stays queued unless a test finishes it.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ExportQuotaTests(ApiFactory factory)
{
    private static readonly object Body = new { dataset = "score_results", columns = new[] { "instrument_code" } };

    private static async Task<HttpResponseMessage> RequestAsync(HttpClient client, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = JsonContent.Create(Body, options: ApiJson.Options),
        };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await response.ProblemCodeAsync(TestContext.Current.CancellationToken));
    }

    private Task FailAsync(string subject) =>
        factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<FamilyResilienceDbContext>();
            var active = await db.ExportJobs
                .Where(job => job.RequestedBy == subject && job.Status == ExportStatus.Queued)
                .ToListAsync(TestContext.Current.CancellationToken);
            foreach (var job in active)
            {
                Assert.True(job.Fail("analytics.export_stalled", TestData.Now).IsSuccess);
            }
        }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_third_active_export_is_refused_with_export_limit_reached()
    {
        using var client = factory.AsResearcher();
        using var first = await RequestAsync(client);
        using var second = await RequestAsync(client);

        using var third = await RequestAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        await AssertRefusedAsync(third, "analytics.export_limit_reached");
    }

    [Fact]
    public async Task Finished_exports_do_not_count_as_active()
    {
        var subject = ApiClients.NewSubject("researcher");
        using var client = factory.AsResearcher(subject);
        using var first = await RequestAsync(client);
        using var second = await RequestAsync(client);
        await FailAsync(subject);

        using var third = await RequestAsync(client);

        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
    }

    [Fact]
    public async Task The_daily_quota_refuses_with_export_daily_limit_reached()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Exports:MaxPerRequesterPerDay"] = "3" })));
        var subject = ApiClients.NewSubject("researcher");
        using var client = Researcher(host, subject);
        for (var request = 0; request < 3; request++)
        {
            using var accepted = await RequestAsync(client);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await FailAsync(subject);
        }

        using var refused = await RequestAsync(client);

        await AssertRefusedAsync(refused, "analytics.export_daily_limit_reached");
    }

    [Fact]
    public async Task A_retry_with_the_same_idempotency_key_is_replayed_even_when_the_quota_is_full()
    {
        using var client = factory.AsResearcher();
        var key = Guid.NewGuid();
        using var first = await RequestAsync(client, key);
        using var second = await RequestAsync(client);

        using var retry = await RequestAsync(client, key);

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
    }

    [Fact]
    public async Task Quotas_are_per_researcher()
    {
        using var busy = factory.AsResearcher();
        using var other = factory.AsResearcher();
        using var first = await RequestAsync(busy);
        using var second = await RequestAsync(busy);

        using var response = await RequestAsync(other);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_request_is_not_recorded()
    {
        var subject = ApiClients.NewSubject("researcher");
        using var client = factory.AsResearcher(subject);
        using var first = await RequestAsync(client);
        using var second = await RequestAsync(client);

        using var third = await RequestAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal(2, await factory.QueryAsync(db => db.ExportJobs.CountAsync(job => job.RequestedBy == subject, TestContext.Current.CancellationToken)));
        Assert.Equal(2, await factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.requested" && audit.ActorId == subject, TestContext.Current.CancellationToken)));
    }

    private static HttpClient Researcher(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "researcher");
        return client;
    }
}
