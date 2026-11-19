using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Infrastructure.Exports;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>
/// Research exports against the analytics population (<see cref="AnalyticsApi"/>): 19 completed sessions of members with
/// research-use consent (11 band_high, 1 incomplete, 5 band_low, 2 band_mid), two completed sessions without consent,
/// a retired scoring rule set that also scored the two 65-year-olds, and a consenting household that withdrew.
/// </summary>
[Collection(AnalyticsApiCollection.Name)]
public sealed partial class ExportEndpointsTests(AnalyticsApi api)
{
    private const int ConsentedRows = 19;

    private ApiFactory Factory => api.Factory;

    private static object Export(string dataset, params string[] columns) => new { dataset, columns };

    private static async Task<HttpResponseMessage> RequestAsync(HttpClient client, object body, Guid key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = JsonContent.Create(body, options: ApiJson.Options),
        };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<ExportResponse> CreatedAsync(HttpClient client, object body, CancellationToken cancellationToken)
    {
        using var response = await RequestAsync(client, body, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadAsync<ExportResponse>(cancellationToken);
    }

    private static async Task<ExportResponse> StatusAsync(HttpClient client, Guid exportId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/v1/admin/exports/{exportId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ExportResponse>(cancellationToken);
    }

    /// <summary>Runs the outbox until no due message of a handled type is left, as the dispatcher would.</summary>
    private static async Task DrainOutboxAsync(WebApplicationFactory<Program> host, CancellationToken cancellationToken)
    {
        for (var round = 0; round < 200; round++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessNextAsync(cancellationToken))
            {
                return;
            }
        }

        Assert.Fail("The outbox did not drain.");
    }

    private static async Task<T> InScopeAsync<T>(WebApplicationFactory<Program> host, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private static Task<ExportJob> JobAsync(ApiFactory factory, Guid exportId, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.ExportJobs.AsNoTracking().SingleAsync(job => job.Id == ExportJobId.From(exportId), cancellationToken));

    /// <summary>The CSV's lines split into cells; every value this platform exports is a code or a number, never quoted.</summary>
    private static async Task<IReadOnlyList<string[]>> CsvAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        return text[..^2].Split("\r\n").Select(line => line.Split(',')).ToList();
    }

    private static HttpClient ResearcherClient(WebApplicationFactory<Program> host, string subject)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Researcher);
        return client;
    }

    private WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings.Select(setting => KeyValuePair.Create(setting.Key, (string?)setting.Value)))));

    private WebApplicationFactory<Program> WithStore(IExportFileStore store) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(store)));

    [Fact]
    public async Task Requesting_an_export_returns_202_with_its_location_and_queues_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);

        using var response = await RequestAsync(client, Export("score_results", "participant_pseudonym", "tier_code"), Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var export = await response.ReadAsync<ExportResponse>(cancellationToken);
        Assert.Equal($"/api/v1/admin/exports/{export.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(
            ("score_results", ExportStatus.Queued, (int?)null, (string?)null, TestData.Now),
            (export.Dataset, export.Status, export.RowCount, export.DownloadPath, export.RequestedAt));
        Assert.Equal(["participant_pseudonym", "tier_code"], export.Columns);
        Assert.Equal(export.Id, (await StatusAsync(client, export.Id, cancellationToken)).Id);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.requested" && audit.ActorId == subject && audit.ResourceId == export.Id.ToString(),
            cancellationToken)));
    }

    [Fact]
    public async Task A_score_export_holds_the_results_of_approved_rule_sets_for_consenting_members_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);
        var export = await CreatedAsync(
            client, Export("score_results", "participant_pseudonym", "tier_code", "score_status", "age_band"), cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);
        var completed = await StatusAsync(client, export.Id, cancellationToken);
        using var download = await client.GetAsync(completed.DownloadPath, cancellationToken);

        Assert.Equal(
            (ExportStatus.Completed, (int?)ConsentedRows, (DateTimeOffset?)TestData.Now, (DateTimeOffset?)TestData.Now.AddDays(ExportSettings.DefaultRetentionDays)),
            (completed.Status, completed.RowCount, completed.FinishedAt, completed.ExpiresAt));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/csv", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Equal($"azwah-export-{export.Id}.csv", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var lines = await CsvAsync(download, cancellationToken);
        Assert.Equal(["participant_pseudonym", "tier_code", "score_status", "age_band"], lines[0]);
        var rows = lines.Skip(1).ToList();
        Assert.Equal(ConsentedRows, rows.Count);
        Assert.Equal(ConsentedRows, rows.Select(row => row[0]).Distinct().Count());
        Assert.All(rows, row => Assert.Matches(Pseudonym(), row[0]));
        Assert.Equal(
            new Dictionary<string, int> { [""] = 1, ["band_high"] = 11, ["band_low"] = 5, ["band_mid"] = 2 },
            rows.GroupBy(row => row[1]).ToDictionary(group => group.Key, group => group.Count()));
        Assert.Equal(1, rows.Count(row => row[2] == "incomplete"));
        Assert.Equal(
            new Dictionary<string, int> { ["30_44"] = 12, ["45_59"] = 5, ["60_plus"] = 2 },
            rows.GroupBy(row => row[3]).ToDictionary(group => group.Key, group => group.Count()));
        foreach (var action in new[] { "analytics.export.completed", "analytics.export.downloaded" })
        {
            Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
                audit => audit.Action == action && audit.ResourceId == export.Id.ToString(), cancellationToken)));
        }
    }

    [Fact]
    public async Task A_session_export_lists_submitted_sessions_with_coarse_values_and_no_identifiers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(
            client,
            Export("assessment_sessions", "household_pseudonym", "submitted_month", "session_status", "answered_count", "employment_status", "is_head_of_household"),
            cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);
        using var download = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        var body = await download.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotMatch(UuidPattern(), body);
        var rows = (await CsvAsync(download, cancellationToken)).Skip(1).ToList();
        Assert.Equal(ConsentedRows, rows.Count);
        Assert.Equal(ConsentedRows, rows.Select(row => row[0]).Distinct().Count());
        Assert.All(rows, row => Assert.Equal(["2026-10", "scored", "4", "true"], new[] { row[1], row[2], row[3], row[5] }));
        Assert.Equal(
            new Dictionary<string, int> { ["employed"] = 17, ["retired"] = 2 },
            rows.GroupBy(row => row[4]).ToDictionary(group => group.Key, group => group.Count()));
    }

    // total_score is numeric(9,3): every synthetic total here is stored as 4.000.
    [Fact]
    public async Task A_score_export_writes_totals_without_trailing_zeros_and_says_why_a_result_is_incomplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(
            client, Export("score_results", "score_status", "incomplete_reason", "total_score", "excluded_count"), cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);
        using var download = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        var lines = await CsvAsync(download, cancellationToken);
        Assert.Equal(["score_status", "incomplete_reason", "total_score", "excluded_count"], lines[0]);
        var rows = lines.Skip(1).Select(row => string.Join('|', row)).ToList();
        Assert.Equal(ConsentedRows, rows.Count);
        Assert.Equal(
            new Dictionary<string, int> { ["complete||4|0"] = 18, ["incomplete|synthetic_incomplete||0"] = 1 },
            rows.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count()));
    }

    [Fact]
    public async Task A_session_export_counts_the_answers_that_were_prefer_not_to_answer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("assessment_sessions", "answered_count", "non_substantive_count"), cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);
        using var download = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        var rows = (await CsvAsync(download, cancellationToken)).Skip(1).ToList();
        Assert.Equal(ConsentedRows, rows.Count);
        // The synthetic version offers no 99, so none of the four answers of any session is non-substantive.
        Assert.All(rows, row => Assert.Equal(["4", "0"], row));
    }

    [Fact]
    public async Task Filters_narrow_an_export_to_one_wave_or_one_instrument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var waveOne = await CreatedAsync(
            client, new { dataset = "score_results", columns = new[] { "tier_code" }, filters = new { waveId = api.WaveOne.Id.Value } }, cancellationToken);
        var waveTwo = await CreatedAsync(
            client, new { dataset = "score_results", columns = new[] { "tier_code" }, filters = new { waveId = api.WaveTwo.Id.Value } }, cancellationToken);
        // At most two of a researcher's exports may be queued at once (ExportQuotas).
        await DrainOutboxAsync(Factory, cancellationToken);
        var otherInstrument = await CreatedAsync(
            client, new { dataset = "assessment_sessions", columns = new[] { "age_band" }, filters = new { instrumentCode = "no_such_instrument" } }, cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);

        Assert.Equal((int?)ConsentedRows, (await StatusAsync(client, waveOne.Id, cancellationToken)).RowCount);
        Assert.Equal((int?)0, (await StatusAsync(client, waveTwo.Id, cancellationToken)).RowCount);
        var empty = await StatusAsync(client, otherInstrument.Id, cancellationToken);
        Assert.Equal((ExportStatus.Completed, (int?)0), (empty.Status, empty.RowCount));
        Assert.Equal(new ExportFiltersResponse(null, "no_such_instrument"), empty.Filters);
    }

    [Fact]
    public async Task A_household_that_withdrew_from_the_study_is_left_out_of_both_datasets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var sessions = await CreatedAsync(
            client, new { dataset = "assessment_sessions", columns = new[] { "age_band" }, filters = new { waveId = api.WaveThree.Id.Value } }, cancellationToken);
        var scores = await CreatedAsync(
            client, new { dataset = "score_results", columns = new[] { "tier_code" }, filters = new { waveId = api.WaveThree.Id.Value } }, cancellationToken);

        await DrainOutboxAsync(Factory, cancellationToken);

        Assert.Equal((int?)0, (await StatusAsync(client, sessions.Id, cancellationToken)).RowCount);
        Assert.Equal((int?)0, (await StatusAsync(client, scores.Id, cancellationToken)).RowCount);
    }

    [Fact]
    public async Task Downloading_before_the_file_is_written_returns_409_not_ready()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);

        using var response = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("analytics.export_not_ready", await response.ProblemCodeAsync(cancellationToken));
    }

    public static TheoryData<string, string> RefusedExports => new()
    {
        { """{"dataset":"answers","columns":["tier_code"]}""", "analytics.export_dataset_invalid" },
        { """{"dataset":"score_results","columns":["member_id"]}""", "analytics.export_column_invalid" },
        { """{"dataset":"assessment_sessions","columns":["tier_code"]}""", "analytics.export_column_invalid" },
        { """{"dataset":"score_results","columns":["tier_code","tier_code"]}""", "analytics.export_column_invalid" },
        { """{"dataset":"score_results","columns":["tier_code"],"filters":{"waveId":"0196b0a3-0000-7000-8000-000000000000"}}""", "analytics.export_filter_invalid" },
        { """{"dataset":"score_results","columns":["tier_code"],"filters":{"instrumentCode":"Not A Code"}}""", "analytics.export_filter_invalid" },
    };

    [Theory]
    [MemberData(nameof(RefusedExports))]
    public async Task A_dataset_column_or_filter_outside_the_allowlists_returns_400_with_its_code(string body, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ProblemCodeAsync(cancellationToken));
    }

    [Theory]
    [InlineData("""{"columns":["tier_code"]}""", "dataset")]
    [InlineData("""{"dataset":"score_results"}""", "columns")]
    [InlineData("""{"dataset":"score_results","columns":[]}""", "columns")]
    public async Task A_malformed_request_returns_400_keyed_by_the_field(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_same_export_and_a_reused_key_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var key = Guid.NewGuid();

        using var first = await RequestAsync(client, Export("score_results", "tier_code"), key, cancellationToken);
        using var retry = await RequestAsync(client, Export("score_results", "tier_code"), key, cancellationToken);
        using var reused = await RequestAsync(client, Export("score_results", "age_band"), key, cancellationToken);
        using var missing = await client.PostJsonAsync("/api/v1/admin/exports", Export("score_results", "tier_code"), cancellationToken);

        var created = await first.ReadAsync<ExportResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(created.Id, (await retry.ReadAsync<ExportResponse>(cancellationToken)).Id);
        Assert.Equal(1, await Factory.QueryAsync(db => db.ExportJobs.CountAsync(job => job.Id == ExportJobId.From(created.Id), cancellationToken)));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("requests.idempotency_key_reused", await reused.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("idempotencyKey", await missing.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Another_researcher_cannot_see_or_download_an_export()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.AsResearcher();
        var export = await CreatedAsync(owner, Export("score_results", "tier_code"), cancellationToken);
        await DrainOutboxAsync(Factory, cancellationToken);
        using var other = Factory.AsResearcher();

        using var status = await other.GetAsync($"/api/v1/admin/exports/{export.Id}", cancellationToken);
        using var file = await other.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);
        using var unknown = await owner.GetAsync($"/api/v1/admin/exports/{Guid.NewGuid()}", cancellationToken);

        foreach (var response in new[] { status, file, unknown })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("analytics.export_not_found", await response.ProblemCodeAsync(cancellationToken));
        }

        Assert.Equal(0, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.downloaded" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [Theory]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SystemAdministrator)]
    [InlineData(Roles.SupportCoordinator)]
    [InlineData(Roles.Participant)]
    public async Task Only_researchers_request_and_read_exports(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var researcher = Factory.AsResearcher();
        var export = await CreatedAsync(researcher, Export("score_results", "tier_code"), cancellationToken);
        using var client = Factory.As(ApiClients.NewSubject("caller"), role);

        using var create = await RequestAsync(client, Export("score_results", "tier_code"), Guid.NewGuid(), cancellationToken);
        using var read = await client.GetAsync($"/api/v1/admin/exports/{export.Id}", cancellationToken);
        using var file = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, file.StatusCode);
    }

    [Fact]
    public async Task A_failed_export_deletes_a_file_an_earlier_attempt_wrote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = WithSettings(("Exports:MaxRows", "5"));
        using var client = ResearcherClient(host, ApiClients.NewSubject("researcher"));
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        var local = new LocalExportFileStore(Factory.ExportDirectory);
        // As if an earlier attempt wrote the file and then failed to record its completion.
        await local.WriteAsync($"exports/{export.Id}.csv", (stream, token) => stream.WriteAsync("tier_code\r\n"u8.ToArray(), token).AsTask(), cancellationToken);

        await InScopeAsync(host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.Equal("analytics.export_too_large", (await StatusAsync(client, export.Id, cancellationToken)).ErrorCode);
        Assert.Null(await local.OpenReadAsync($"exports/{export.Id}.csv", cancellationToken));
    }

    [Fact]
    public async Task Storage_switched_off_before_processing_fails_the_export()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        await using var unconfigured = WithSettings(("Exports:LocalDirectory", ""));

        var processed = await InScopeAsync(unconfigured, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.True(processed.IsSuccess);
        var failed = await StatusAsync(client, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Failed, "analytics.export_storage_not_configured"), (failed.Status, failed.ErrorCode));
    }

    [Fact]
    public async Task A_completed_export_whose_file_is_gone_returns_409_expired_and_records_no_download()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        await DrainOutboxAsync(Factory, cancellationToken);
        await new LocalExportFileStore(Factory.ExportDirectory).DeleteAsync($"exports/{export.Id}.csv", cancellationToken);

        using var response = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("analytics.export_expired", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(0, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.downloaded" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task A_run_whose_job_maintenance_failed_meanwhile_leaves_no_file_behind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        await using var later = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now.AddHours(ExportSettings.DefaultStalledAfterHours + 1)))));
        var local = new LocalExportFileStore(Factory.ExportDirectory);
        // Maintenance runs while the rows are being read, finds the job unchanged for too long and fails it.
        await using var host = WithStore(new InterruptedExportFileStore(
            local, () => InScopeAsync(later, services => services.GetRequiredService<ExpireExports>().HandleAsync(cancellationToken))));

        var processed = await InScopeAsync(host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.True(processed.IsFailure);
        var failed = await StatusAsync(client, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Failed, "analytics.export_stalled"), (failed.Status, failed.ErrorCode));
        Assert.Null(await local.OpenReadAsync($"exports/{export.Id}.csv", cancellationToken));
    }

    [Fact]
    public async Task Maintenance_leaves_an_export_alone_while_it_is_within_its_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);

        await InScopeAsync(Factory, services => services.GetRequiredService<ExpireExports>().HandleAsync(cancellationToken));

        Assert.Equal(ExportStatus.Queued, (await StatusAsync(client, export.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Maintenance_fails_an_export_that_has_been_stuck_for_a_day()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        await using var later = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now.AddHours(ExportSettings.DefaultStalledAfterHours + 1)))));

        var swept = await InScopeAsync(later, services => services.GetRequiredService<ExpireExports>().HandleAsync(cancellationToken));

        Assert.True(swept.Value >= 1);
        var stuck = await StatusAsync(client, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Failed, "analytics.export_stalled"), (stuck.Status, stuck.ErrorCode));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.failed" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(null);

        using var create = await RequestAsync(client, Export("score_results", "tier_code"), Guid.NewGuid(), cancellationToken);
        using var file = await client.GetAsync($"/api/v1/admin/exports/{Guid.NewGuid()}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, file.StatusCode);
    }

    [Fact]
    public async Task Without_configured_storage_an_export_is_refused_with_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = WithSettings(("Exports:LocalDirectory", ""));
        using var client = ResearcherClient(host, ApiClients.NewSubject("researcher"));

        using var response = await RequestAsync(client, Export("score_results", "tier_code"), Guid.NewGuid(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("analytics.export_storage_not_configured", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task An_export_over_the_row_limit_fails_as_too_large()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = WithSettings(("Exports:MaxRows", "5"));
        using var client = ResearcherClient(host, ApiClients.NewSubject("researcher"));
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);

        var processed = await InScopeAsync(host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.True(processed.IsSuccess);
        var failed = await StatusAsync(client, export.Id, cancellationToken);
        Assert.Equal(
            (ExportStatus.Failed, "analytics.export_too_large", (int?)null, (string?)null),
            (failed.Status, failed.ErrorCode, failed.RowCount, failed.DownloadPath));
        Assert.Null(await InScopeAsync(host, services => services.GetRequiredService<IExportFileStore>().OpenReadAsync($"exports/{export.Id}.csv", cancellationToken)));
    }

    [Fact]
    public async Task Processing_a_completed_export_again_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "participant_pseudonym", "tier_code"), cancellationToken);
        await DrainOutboxAsync(Factory, cancellationToken);
        using var before = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);
        var original = await before.Content.ReadAsStringAsync(cancellationToken);

        var again = await InScopeAsync(Factory, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.True(again.IsSuccess);
        var job = await JobAsync(Factory, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Completed, 1, (int?)ConsentedRows), (job.Status, job.Attempts, job.RowCount));
        using var after = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);
        Assert.Equal(original, await after.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task A_crash_while_writing_is_retried_and_the_export_completes_once_with_one_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var local = new LocalExportFileStore(Factory.ExportDirectory);
        await using var host = WithStore(new CrashingExportFileStore(local, crashes: 1));
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "participant_pseudonym", "tier_code"), cancellationToken);

        await Assert.ThrowsAsync<IOException>(() => InScopeAsync(
            host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken)));
        var crashed = await JobAsync(Factory, export.Id, cancellationToken);
        var leftover = await local.OpenReadAsync($"exports/{export.Id}.csv", cancellationToken);
        var retried = await InScopeAsync(host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.Equal((ExportStatus.Running, 1), (crashed.Status, crashed.Attempts));
        Assert.Null(leftover);
        Assert.True(retried.IsSuccess);
        var job = await JobAsync(Factory, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Completed, 2, (int?)ConsentedRows), (job.Status, job.Attempts, job.RowCount));
        using var download = await client.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);
        Assert.Equal(ConsentedRows + 1, (await CsvAsync(download, cancellationToken)).Count);
        Assert.Single(Directory.GetFiles(Path.Combine(Factory.ExportDirectory, "exports"), $"{export.Id}*"));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.completed" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task An_export_that_keeps_crashing_fails_after_its_last_attempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = WithStore(new CrashingExportFileStore(new LocalExportFileStore(Factory.ExportDirectory), crashes: int.MaxValue));
        using var client = Factory.AsResearcher();
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);

        for (var attempt = 1; attempt <= ExportJob.MaxAttempts; attempt++)
        {
            await Assert.ThrowsAsync<IOException>(() => InScopeAsync(
                host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken)));
        }

        var last = await InScopeAsync(host, services => services.GetRequiredService<ProcessExport>().HandleAsync(export.Id, cancellationToken));

        Assert.True(last.IsSuccess);
        var failed = await StatusAsync(client, export.Id, cancellationToken);
        Assert.Equal((ExportStatus.Failed, "analytics.export_attempts_exhausted"), (failed.Status, failed.ErrorCode));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.failed" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task An_expired_export_cannot_be_downloaded_and_maintenance_deletes_its_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);
        var export = await CreatedAsync(client, Export("score_results", "tier_code"), cancellationToken);
        await DrainOutboxAsync(Factory, cancellationToken);
        await using var later = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now.AddDays(ExportSettings.DefaultRetentionDays + 1)))));
        using var laterClient = ResearcherClient(later, subject);

        using var tooLate = await laterClient.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);
        var expired = await InScopeAsync(later, services => services.GetRequiredService<ExpireExports>().HandleAsync(cancellationToken));
        using var afterExpiry = await laterClient.GetAsync($"/api/v1/admin/exports/{export.Id}/file", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Equal("analytics.export_expired", await tooLate.ProblemCodeAsync(cancellationToken));
        Assert.True(expired.Value >= 1);
        Assert.Equal(ExportStatus.Expired, (await JobAsync(Factory, export.Id, cancellationToken)).Status);
        Assert.False(File.Exists(Path.Combine(Factory.ExportDirectory, "exports", $"{export.Id}.csv")));
        Assert.Equal("analytics.export_expired", await afterExpiry.ProblemCodeAsync(cancellationToken));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "analytics.export.expired" && audit.ResourceId == export.Id.ToString(), cancellationToken)));
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Pseudonym();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UuidPattern();

    /// <summary>Writes the whole file, then fails before it is moved into place, the first <c>crashes</c> times.</summary>
    private sealed class CrashingExportFileStore(IExportFileStore inner, int crashes) : IExportFileStore
    {
        private int _remaining = crashes;

        public bool IsConfigured => true;

        public Task WriteAsync(string key, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default) =>
            Interlocked.Decrement(ref _remaining) >= 0
                ? inner.WriteAsync(
                    key,
                    async (stream, token) =>
                    {
                        await write(stream, token);
                        throw new IOException("A synthetic crash after the rows were written.");
                    },
                    cancellationToken)
                : inner.WriteAsync(key, write, cancellationToken);

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
    }

    /// <summary>Runs <paramref name="beforeWrite"/> (another process's work, such as maintenance) just before each write.</summary>
    private sealed class InterruptedExportFileStore(IExportFileStore inner, Func<Task> beforeWrite) : IExportFileStore
    {
        public bool IsConfigured => true;

        public async Task WriteAsync(string key, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
        {
            await beforeWrite();
            await inner.WriteAsync(key, write, cancellationToken);
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
    }
}
