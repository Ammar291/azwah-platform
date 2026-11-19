using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Households;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// The retention job (D-88) deletes nothing until it is switched on and given periods; each period removes only what is
/// past it. The test host's clock is <see cref="TestData.Now"/>; rows are made old by moving their times back.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RetentionTests(ApiFactory factory)
{
    private WebApplicationFactory<Program> Host(Dictionary<string, string?> settings) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));

    private static async Task<RetentionReport> RunAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ApplyRetention>().HandleAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        var connection = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", TestData.Now);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A household that withdrew <paramref name="daysAgo"/> days before the test clock.</summary>
    private async Task<Guid> WithdrawnHouseholdAsync(int daysAgo)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);
        using var withdrawn = await client.PatchJsonAsync($"/api/v1/households/{household.Id}", new { status = "withdrawn" }, cancellationToken);
        await ExecuteAsync($"UPDATE households SET updated_at = @now - interval '{daysAgo} days' WHERE id = @id", household.Id);
        return household.Id;
    }

    private Task<bool> ExistsAsync(Guid householdId) =>
        factory.QueryAsync(db => db.Households.AnyAsync(household => household.Id == HouseholdId.From(householdId), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Switched_off_it_deletes_nothing_even_with_periods()
    {
        var old = await WithdrawnHouseholdAsync(400);
        await using var host = Host(new()
        {
            ["Retention:WithdrawnHouseholdDays"] = "30",
            ["Erasure:ConsentRecords"] = "delete",
        });

        var report = await RunAsync(host);

        Assert.Equal(RetentionReport.Nothing, report);
        Assert.True(await ExistsAsync(old));
    }

    [Fact]
    public async Task Switched_on_without_periods_it_deletes_nothing()
    {
        var old = await WithdrawnHouseholdAsync(400);
        await using var host = Host(new() { ["Retention:Enabled"] = "true", ["Erasure:ConsentRecords"] = "delete" });

        var report = await RunAsync(host);

        Assert.Equal((0, 0, 0), (report.HouseholdsErased, report.OutboxMessagesDeleted, report.ExportJobsDeleted));
        Assert.True(await ExistsAsync(old));
    }

    [Fact]
    public async Task Withdrawn_households_past_the_period_are_erased_and_recent_or_active_ones_stay()
    {
        var old = await WithdrawnHouseholdAsync(31);
        var recent = await WithdrawnHouseholdAsync(29);
        using var client = factory.AsParticipant();
        var active = await client.CreateHouseholdAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync("UPDATE households SET updated_at = @now - interval '400 days' WHERE id = @id", active.Id);
        await using var host = Host(new()
        {
            ["Retention:Enabled"] = "true",
            ["Retention:WithdrawnHouseholdDays"] = "30",
            ["Erasure:ConsentRecords"] = "delete",
        });

        var report = await RunAsync(host);

        Assert.True(report.HouseholdsErased >= 1);
        Assert.False(await ExistsAsync(old));
        Assert.True(await ExistsAsync(recent));
        Assert.True(await ExistsAsync(active.Id));
    }

    [Fact]
    public async Task One_household_that_cannot_be_erased_is_counted_and_the_run_goes_on()
    {
        var faulty = await WithdrawnHouseholdAsync(900);
        var next = await WithdrawnHouseholdAsync(800);
        await using var host = Host(new()
        {
            ["Retention:Enabled"] = "true",
            ["Retention:WithdrawnHouseholdDays"] = "30",
            ["Erasure:ConsentRecords"] = "delete",
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IHouseholdErasure>(provider => new FaultyFor(
                HouseholdId.From(faulty),
                ActivatorUtilities.CreateInstance<Infrastructure.Persistence.Erasure.HouseholdErasure>(provider)))));

        var report = await RunAsync(host);

        Assert.Equal(["exception:InvalidOperationException"], report.Failures);
        Assert.True(await ExistsAsync(faulty));
        Assert.False(await ExistsAsync(next));
    }

    /// <summary>Fails for one household, as a database fault would, and erases the others.</summary>
    private sealed class FaultyFor(HouseholdId faulty, IHouseholdErasure inner) : IHouseholdErasure
    {
        public Task<bool> LockAsync(HouseholdId householdId, CancellationToken cancellationToken) => inner.LockAsync(householdId, cancellationToken);

        public Task<IReadOnlyDictionary<string, int>> EraseAsync(HouseholdId householdId, CancellationToken cancellationToken) =>
            householdId == faulty
                ? throw new InvalidOperationException("Simulated database fault.")
                : inner.EraseAsync(householdId, cancellationToken);
    }

    [Fact]
    public async Task Households_are_not_erased_while_the_consent_record_rule_is_unset()
    {
        var old = await WithdrawnHouseholdAsync(400);
        await using var host = Host(new() { ["Retention:Enabled"] = "true", ["Retention:WithdrawnHouseholdDays"] = "30" });

        var report = await RunAsync(host);

        Assert.True(report.ErasureNotConfigured);
        Assert.Equal(0, report.HouseholdsErased);
        Assert.True(await ExistsAsync(old));
    }

    [Fact]
    public async Task Processed_outbox_messages_past_the_period_are_deleted_and_pending_ones_stay()
    {
        var oldMessage = Guid.CreateVersion7();
        var recentMessage = Guid.CreateVersion7();
        var pendingMessage = Guid.CreateVersion7();
        foreach (var (id, status, daysAgo) in new[] { (oldMessage, "Processed", 31), (recentMessage, "Processed", 1), (pendingMessage, "Pending", 400) })
        {
            await ExecuteAsync(
                $"""
                INSERT INTO outbox_messages (id, type, payload, occurred_at, status, retry_count, processed_at, next_attempt_at)
                VALUES (@id, 'retention.test.v1', jsonb_build_object(), @now - interval '{daysAgo} days', '{status}', 0,
                        CASE WHEN '{status}' = 'Processed' THEN @now - interval '{daysAgo} days' END, @now)
                """,
                id);
        }

        await using var host = Host(new() { ["Retention:Enabled"] = "true", ["Retention:ProcessedOutboxDays"] = "30" });

        var report = await RunAsync(host);

        Assert.True(report.OutboxMessagesDeleted >= 1);
        var remaining = await factory.QueryAsync(db => db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM outbox_messages WHERE type = 'retention.test.v1'")
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(oldMessage, remaining);
        Assert.Contains(recentMessage, remaining);
        Assert.Contains(pendingMessage, remaining);
    }

    [Fact]
    public async Task Failed_or_expired_export_jobs_past_the_period_are_deleted_and_running_ones_stay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        var jobs = await factory.InScopeAsync(
            services =>
            {
                var repository = services.GetRequiredService<Application.Analytics.IExportJobRepository>();
                var made = new List<Domain.Analytics.ExportJob>();
                for (var index = 0; index < 3; index++)
                {
                    var job = TestData.Ok(Domain.Analytics.ExportJob.Request(
                        subject, Domain.Analytics.ExportDataset.ScoreResults, ["instrument_code"], null, null, TestData.Now));
                    if (index < 2)
                    {
                        TestData.Ok(job.Fail("analytics.export_stalled", TestData.Now));
                    }

                    repository.Add(job);
                    made.Add(job);
                }

                return Task.FromResult(made.Select(job => job.Id.Value).ToArray());
            },
            cancellationToken);
        await ExecuteAsync("UPDATE export_jobs SET updated_at = @now - interval '31 days' WHERE id = @id", jobs[0]);
        await ExecuteAsync("UPDATE export_jobs SET updated_at = @now - interval '31 days' WHERE id = @id", jobs[2]);
        await using var host = Host(new() { ["Retention:Enabled"] = "true", ["Retention:FinishedExportJobsDays"] = "30" });

        var report = await RunAsync(host);

        Assert.True(report.ExportJobsDeleted >= 1);
        var remaining = await factory.QueryAsync(db => db.ExportJobs.Where(job => job.RequestedBy == subject).Select(job => job.Id.Value)
            .ToListAsync(cancellationToken));
        Assert.Equal(jobs.Skip(1).Order(), remaining.Order());
    }
}
