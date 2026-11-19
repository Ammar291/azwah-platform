using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Analytics;

public sealed class ExportJobTests
{
    private const string Requester = "researcher-subject";

    private static ExportJob Requested() =>
        Ok(ExportJob.Request(Requester, ExportDataset.ScoreResults, ["participant_pseudonym", "tier_code"], null, null, Now));

    [Fact]
    public void Request_queues_the_job_and_raises_export_requested()
    {
        var job = Requested();

        Assert.Equal(
            (Requester, ExportDataset.ScoreResults, ExportStatus.Queued, 0, Now, (DateTimeOffset?)null),
            (job.RequestedBy, job.Dataset, job.Status, job.Attempts, job.RequestedAt, job.StartedAt));
        Assert.Equal(["participant_pseudonym", "tier_code"], job.Columns);
        Assert.Equal(7, job.Id.Value.Version);
        Assert.Equal(job.Id, Assert.IsType<ExportRequested>(Assert.Single(job.GetDomainEvents())).ExportJobId);
        Assert.Equal($"exports/{job.Id.Value}.csv", job.StorageKey);
    }

    public static TheoryData<string, ExportDataset, string[], string?, Error> InvalidRequests => new()
    {
        { "", ExportDataset.ScoreResults, ["tier_code"], null, IdentityErrors.SubjectInvalid },
        { new string('s', ExportJob.MaxRequesterLength + 1), ExportDataset.ScoreResults, ["tier_code"], null, IdentityErrors.SubjectInvalid },
        { Requester, (ExportDataset)99, ["tier_code"], null, AnalyticsErrors.ExportDatasetInvalid },
        { Requester, ExportDataset.ScoreResults, [], null, AnalyticsErrors.ExportColumnInvalid },
        { Requester, ExportDataset.ScoreResults, ["tier_code", "tier_code"], null, AnalyticsErrors.ExportColumnInvalid },
        { Requester, ExportDataset.ScoreResults, ["Tier Code"], null, AnalyticsErrors.ExportColumnInvalid },
        { Requester, ExportDataset.ScoreResults, [.. Enumerable.Range(0, ExportJob.MaxColumns + 1).Select(index => $"column_{index}")], null, AnalyticsErrors.ExportColumnInvalid },
        { Requester, ExportDataset.ScoreResults, ["tier_code"], "Not A Code", AnalyticsErrors.ExportFilterInvalid },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Request_refuses_a_malformed_job(string requester, ExportDataset dataset, string[] columns, string? instrumentCode, Error expected) =>
        Assert.Equal(expected, ExportJob.Request(requester, dataset, columns, null, instrumentCode, Now).Error);

    [Fact]
    public void Request_refuses_a_time_that_is_not_utc() =>
        Assert.Equal(
            CommonErrors.TimeNotUtc,
            ExportJob.Request(Requester, ExportDataset.ScoreResults, ["tier_code"], null, null, Now.ToOffset(TimeSpan.FromHours(4))).Error);

    [Fact]
    public void A_job_runs_completes_and_expires()
    {
        var job = Requested();
        var later = Now.AddMinutes(1);

        Ok(job.Begin(Now));
        Assert.Equal((ExportStatus.Running, 1, (DateTimeOffset?)Now), (job.Status, job.Attempts, job.StartedAt));
        Assert.False(job.IsDownloadable(Now));

        Ok(job.Complete(19, Now.AddDays(7), later));
        Assert.Equal(
            (ExportStatus.Completed, (int?)19, (DateTimeOffset?)later, (DateTimeOffset?)Now.AddDays(7), (string?)null),
            (job.Status, job.RowCount, job.FinishedAt, job.ExpiresAt, job.ErrorCode));
        Assert.True(job.IsDownloadable(Now.AddDays(7).AddTicks(-1)));
        Assert.False(job.IsDownloadable(Now.AddDays(7)));

        Ok(job.Expire(Now.AddDays(8)));
        Assert.Equal(ExportStatus.Expired, job.Status);
        Assert.False(job.IsDownloadable(Now));
    }

    [Fact]
    public void A_running_job_begins_again_after_a_crash_until_its_attempts_are_used()
    {
        var job = Requested();
        for (var attempt = 1; attempt <= ExportJob.MaxAttempts; attempt++)
        {
            Ok(job.Begin(Now));
            Assert.Equal(attempt, job.Attempts);
        }

        Assert.Equal(AnalyticsErrors.ExportAttemptsExhausted, job.Begin(Now).Error);
        Assert.Equal(ExportJob.MaxAttempts, job.Attempts);

        Ok(job.Fail(AnalyticsErrors.ExportAttemptsExhausted.Code, Now));
        Assert.Equal((ExportStatus.Failed, AnalyticsErrors.ExportAttemptsExhausted.Code, (DateTimeOffset?)Now), (job.Status, job.ErrorCode, job.FinishedAt));
    }

    [Fact]
    public void A_queued_job_can_fail_before_it_runs()
    {
        var job = Requested();

        Ok(job.Fail(AnalyticsErrors.ExportStorageNotConfigured.Code, Now));

        Assert.Equal((ExportStatus.Failed, 0), (job.Status, job.Attempts));
    }

    [Fact]
    public void Steps_out_of_order_are_refused()
    {
        var queued = Requested();
        var completed = Requested();
        Ok(completed.Begin(Now));
        Ok(completed.Complete(1, Now.AddDays(7), Now));

        Assert.Equal(AnalyticsErrors.ExportTransitionInvalid, queued.Complete(1, Now.AddDays(7), Now).Error);
        Assert.Equal(AnalyticsErrors.ExportTransitionInvalid, queued.Expire(Now).Error);
        Assert.Equal(AnalyticsErrors.ExportTransitionInvalid, completed.Begin(Now).Error);
        Assert.Equal(AnalyticsErrors.ExportTransitionInvalid, completed.Fail(AnalyticsErrors.ExportTooLarge.Code, Now).Error);
        Assert.Equal(AnalyticsErrors.ExportTransitionInvalid, completed.Complete(1, Now.AddDays(7), Now).Error);
        Assert.Equal(ExportStatus.Completed, completed.Status);
    }

    [Fact]
    public void Completing_needs_a_row_count_and_an_expiry_after_now()
    {
        var job = Requested();
        Ok(job.Begin(Now));

        Assert.Throws<ArgumentOutOfRangeException>(() => job.Complete(-1, Now.AddDays(7), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Complete(1, Now, Now));
        Assert.Throws<ArgumentException>(() => job.Fail("Not a code", Now));
    }
}
