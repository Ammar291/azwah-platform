using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ExportJobPersistenceTests(DatabaseFixture fixture)
{
    private async Task<ExportJob> SavedAsync(ExportJob job, CancellationToken cancellationToken)
    {
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IExportJobRepository>().Add(job);
                return Task.CompletedTask;
            },
            cancellationToken);
        return job;
    }

    private static ExportJob Requested(string? instrumentCode = null) =>
        TestData.Ok(ExportJob.Request(
            ApiClients.NewSubject("researcher"), ExportDataset.ScoreResults, ["participant_pseudonym", "tier_code"], null, instrumentCode, TestData.Now));

    [Fact]
    public async Task A_job_is_read_back_with_its_columns_filters_and_requested_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await SavedAsync(Requested("test_instrument"), cancellationToken);

        var saved = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportJobRepository>().GetAsync(job.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(saved);
        Assert.Equal(
            (job.RequestedBy, ExportDataset.ScoreResults, ExportStatus.Queued, 0, "test_instrument", TestData.Now),
            (saved.RequestedBy, saved.Dataset, saved.Status, saved.Attempts, saved.InstrumentCode, saved.RequestedAt));
        Assert.Equal(["participant_pseudonym", "tier_code"], saved.Columns);
        Assert.Single(await fixture.OutboxMessagesAsync("analytics.export_requested.v1", "exportJobId", job.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task Only_completed_jobs_past_their_expiry_are_listed_as_expired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var due = Requested();
        TestData.Ok(due.Begin(TestData.Now));
        TestData.Ok(due.Complete(3, TestData.Now.AddDays(1), TestData.Now));
        var live = Requested();
        TestData.Ok(live.Begin(TestData.Now));
        TestData.Ok(live.Complete(3, TestData.Now.AddDays(30), TestData.Now));
        var queued = Requested();
        await SavedAsync(due, cancellationToken);
        await SavedAsync(live, cancellationToken);
        await SavedAsync(queued, cancellationToken);

        var expired = await fixture.InScopeAsync(
            services => services.GetRequiredService<IExportJobRepository>().ListExpiredAsync(TestData.Now.AddDays(2), 100, cancellationToken),
            cancellationToken);

        Assert.Contains(expired, job => job.Id == due.Id);
        Assert.DoesNotContain(expired, job => job.Id == live.Id || job.Id == queued.Id);
    }

    [Fact]
    public async Task The_database_refuses_a_completed_job_without_its_row_count()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var job = await SavedAsync(Requested(), cancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.ExecuteSqlAsync(
            $"UPDATE export_jobs SET status = 'Completed', attempts = 1, started_at = now(), finished_at = now(), expires_at = now() WHERE id = {job.Id.Value}",
            cancellationToken));
    }
}
