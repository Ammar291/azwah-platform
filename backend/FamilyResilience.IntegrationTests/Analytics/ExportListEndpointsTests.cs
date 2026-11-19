using System.Net;
using System.Net.Http.Json;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>The researcher's own export list and the export catalog, which the dashboard builds its export form from.</summary>
[Collection(AnalyticsApiCollection.Name)]
public sealed class ExportListEndpointsTests(AnalyticsApi api)
{
    private ApiFactory Factory => api.Factory;

    private static async Task<ExportResponse> RequestAsync(HttpClient client, string dataset, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = JsonContent.Create(new { dataset, columns = new[] { "participant_pseudonym" } }, options: ApiJson.Options),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadAsync<ExportResponse>(cancellationToken);
    }

    [Fact]
    public async Task A_researcher_lists_only_their_own_exports_newest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var mine = Factory.AsResearcher();
        using var theirs = Factory.AsResearcher();
        var first = await RequestAsync(mine, "assessment_sessions", cancellationToken);
        var second = await RequestAsync(mine, "score_results", cancellationToken);
        var other = await RequestAsync(theirs, "score_results", cancellationToken);

        using var response = await mine.GetAsync("/api/v1/admin/exports?page=1&pageSize=10", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResponse<ExportResponse>>(cancellationToken);
        Assert.Equal([second.Id, first.Id], page.Items.Select(export => export.Id));
        Assert.Equal((2, 1, 10), (page.TotalCount, page.Page, page.PageSize));
        Assert.DoesNotContain(page.Items, export => export.Id == other.Id);
        Assert.Equal(("score_results", ExportStatus.Queued), (page.Items[0].Dataset, page.Items[0].Status));
    }

    [Fact]
    public async Task A_researcher_without_exports_gets_an_empty_page()
    {
        using var client = Factory.AsResearcher();

        using var response = await client.GetAsync("/api/v1/admin/exports", TestContext.Current.CancellationToken);

        var page = await response.ReadAsync<PagedResponse<ExportResponse>>(TestContext.Current.CancellationToken);
        Assert.Equal((0, 0, 1, 20), (page.Items.Count, page.TotalCount, page.Page, page.PageSize));
    }

    [Fact]
    public async Task The_list_pages_one_page_at_a_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsResearcher();
        var first = await RequestAsync(client, "assessment_sessions", cancellationToken);
        await RequestAsync(client, "assessment_sessions", cancellationToken);

        using var response = await client.GetAsync("/api/v1/admin/exports?page=2&pageSize=1", cancellationToken);

        var page = await response.ReadAsync<PagedResponse<ExportResponse>>(cancellationToken);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalCount);
    }

    [Theory]
    [InlineData("/api/v1/admin/exports?page=0", "page")]
    [InlineData("/api/v1/admin/exports?pageSize=101", "pageSize")]
    public async Task Paging_out_of_range_returns_400(string url, string key)
    {
        using var client = Factory.AsResearcher();

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_catalog_lists_each_dataset_with_exactly_its_allowed_columns_and_the_filters()
    {
        using var client = Factory.AsResearcher();

        using var response = await client.GetAsync("/api/v1/admin/exports/catalog", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = await response.ReadAsync<ExportCatalogResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(["assessment_sessions", "score_results"], catalog.Datasets.Select(dataset => dataset.Code));
        Assert.Equal(ExportCatalog.ColumnsOf(ExportDataset.AssessmentSessions), catalog.Datasets[0].Columns);
        Assert.Equal(ExportCatalog.ColumnsOf(ExportDataset.ScoreResults), catalog.Datasets[1].Columns);
        Assert.Equal(["wave_id", "instrument_code"], catalog.Filters);
    }

    [Theory]
    [InlineData("/api/v1/admin/exports")]
    [InlineData("/api/v1/admin/exports/catalog")]
    public async Task A_program_administrator_gets_403(string url)
    {
        using var client = Factory.AsProgramAdministrator();

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/exports")]
    [InlineData("/api/v1/admin/exports/catalog")]
    public async Task An_anonymous_caller_gets_401(string url)
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
