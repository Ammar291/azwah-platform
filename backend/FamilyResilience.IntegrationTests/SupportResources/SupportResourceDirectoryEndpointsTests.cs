using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The directory's administration API: system administrators draft, edit, approve and retire versions of the entries
/// the platform may point participants to. Every entry here is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportResourceDirectoryEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/admin/support-resources";

    private static object Draft(string code, bool arabic = true, string? url = null) => new
    {
        code,
        nameAr = arabic ? "مورد تجريبي" : null,
        nameEn = "Synthetic resource",
        summaryAr = arabic ? "ملخص تجريبي" : null,
        summaryEn = "Synthetic summary",
        providerNameAr = arabic ? "جهة تجريبية" : null,
        providerNameEn = "Synthetic provider",
        url = url ?? "https://example.org/synthetic",
        categories = new[] { "synthetic_support" },
        audiences = new[] { "adults" },
        serviceLanguages = new[] { "ar", "en" },
        visibility = "everyone",
    };

    private static object Approval(DateTimeOffset? reviewDueAt = null, string? source = "Synthetic review") => new
    {
        approvalSource = source,
        reviewDueAt = reviewDueAt ?? TestData.Now.AddDays(180),
    };

    private static async Task<SupportResourceResponse> DraftedAsync(
        HttpClient admin, string code, CancellationToken cancellationToken, bool arabic = true)
    {
        using var response = await admin.PostJsonAsync(Route, Draft(code, arabic), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static async Task<SupportResourceResponse> ApprovedAsync(
        HttpClient admin, Guid id, CancellationToken cancellationToken)
    {
        using var response = await admin.PostJsonAsync($"{Route}/{id}/approve", Approval(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static async Task<SupportResourceResponse> GetAsync(HttpClient admin, Guid id, CancellationToken cancellationToken)
    {
        using var response = await admin.GetAsync($"{Route}/{id}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private Task<int> AuditCountAsync(string action, Guid id, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && audit.ResourceType == "support_resource" && audit.ResourceId == id.ToString(),
            cancellationToken));

    [Fact]
    public async Task A_system_administrator_drafts_a_resource_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        using var admin = factory.AsSystemAdministrator();

        using var created = await admin.PostJsonAsync(Route, Draft(code), cancellationToken);
        var resource = await created.ReadAsync<SupportResourceResponse>(cancellationToken);
        var fetched = await GetAsync(admin, resource.Id, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{Route}/{resource.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal(resource.Id, fetched.Id);
        Assert.Equal((code, 1, SupportResourceStatus.Draft, SupportResourceVisibility.Everyone), (resource.Code, resource.VersionNumber, resource.Status, resource.Visibility));
        Assert.Equal(("مورد تجريبي", "Synthetic resource", "ملخص تجريبي", "Synthetic summary"), (resource.NameAr, resource.NameEn, resource.SummaryAr, resource.SummaryEn));
        Assert.Equal(("جهة تجريبية", "Synthetic provider", "https://example.org/synthetic"), (resource.ProviderNameAr, resource.ProviderNameEn, resource.Url));
        Assert.Equal(["synthetic_support"], resource.Categories);
        Assert.Equal(["adults"], resource.Audiences);
        Assert.Equal(["ar", "en"], resource.ServiceLanguages);
        Assert.Null(resource.ApprovedBy);
        Assert.Null(resource.ApprovedAt);
        Assert.Null(resource.ApprovalSource);
        Assert.Null(resource.ReviewDueAt);
        Assert.Null(resource.RetiredAt);
        Assert.False(resource.ReviewOverdue);
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.drafted", resource.Id, cancellationToken));
    }

    [Fact]
    public async Task Drafting_the_same_code_again_creates_the_next_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        using var admin = factory.AsSystemAdministrator();

        var first = await DraftedAsync(admin, code, cancellationToken);
        var second = await DraftedAsync(admin, code, cancellationToken);

        Assert.Equal((1, 2), (first.VersionNumber, second.VersionNumber));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("nameEn")]
    [InlineData("summaryEn")]
    [InlineData("providerNameEn")]
    [InlineData("url")]
    [InlineData("categories")]
    [InlineData("serviceLanguages")]
    [InlineData("visibility")]
    public async Task A_draft_without_its_fields_returns_400_keyed_by_the_field(string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var node = JsonNode.Parse(JsonSerializer.Serialize(Draft(TestData.UniqueCode("resource")), ApiJson.Options))!.AsObject();
        node.Remove(field);

        using var response = await admin.PostRawJsonAsync(Route, node.ToJsonString(), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_malformed_url_returns_400_url_invalid()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();

        using var response = await admin.PostJsonAsync(
            Route, Draft(TestData.UniqueCode("resource"), url: "http://example.org/not-https"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("support_resources.url_invalid", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_capitalised_https_scheme_is_stored_in_lowercase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();

        using var created = await admin.PostJsonAsync(
            Route, Draft(TestData.UniqueCode("resource"), url: "HTTPS://example.org/Synthetic"), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var resource = await created.ReadAsync<SupportResourceResponse>(cancellationToken);
        using var updated = await admin.PutJsonAsync(
            $"{Route}/{resource.Id}", Draft("ignored", url: "Https://example.org/Changed"), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var changed = await updated.ReadAsync<SupportResourceResponse>(cancellationToken);

        Assert.Equal("https://example.org/Synthetic", resource.Url);
        Assert.Equal("https://example.org/Changed", changed.Url);
        Assert.Equal("https://example.org/Changed", (await GetAsync(admin, resource.Id, cancellationToken)).Url);
    }

    [Fact]
    public async Task A_draft_is_updated_until_it_is_approved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var draft = await DraftedAsync(admin, TestData.UniqueCode("resource"), cancellationToken);
        var change = new
        {
            nameAr = "مورد معدل",
            nameEn = "Synthetic changed",
            summaryAr = "ملخص تجريبي",
            summaryEn = "Synthetic summary",
            providerNameAr = "جهة تجريبية",
            providerNameEn = "Synthetic provider",
            url = "https://example.org/changed",
            categories = new[] { "synthetic_support" },
            audiences = Array.Empty<string>(),
            serviceLanguages = new[] { "en" },
            visibility = "staff_only",
        };

        using var updated = await admin.PutJsonAsync($"{Route}/{draft.Id}", change, cancellationToken);
        var body = await updated.ReadAsync<SupportResourceResponse>(cancellationToken);
        await ApprovedAsync(admin, draft.Id, cancellationToken);
        using var refused = await admin.PutJsonAsync($"{Route}/{draft.Id}", change, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(("Synthetic changed", "https://example.org/changed", SupportResourceVisibility.StaffOnly), (body.NameEn, body.Url, body.Visibility));
        Assert.Equal(["en"], body.ServiceLanguages);
        Assert.Empty(body.Audiences);
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.updated", draft.Id, cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("support_resources.not_draft", await refused.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Approving_records_the_approver_and_retires_the_previous_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        using var admin = factory.AsSystemAdministrator();
        var first = await DraftedAsync(admin, code, cancellationToken);
        var firstApproved = await ApprovedAsync(admin, first.Id, cancellationToken);
        var second = await DraftedAsync(admin, code, cancellationToken);

        var secondApproved = await ApprovedAsync(admin, second.Id, cancellationToken);
        var firstNow = await GetAsync(admin, first.Id, cancellationToken);

        Assert.Equal(SupportResourceStatus.Approved, firstApproved.Status);
        Assert.Equal(SupportResourceStatus.Approved, secondApproved.Status);
        Assert.Equal(ApiClients.SystemAdministratorSubject, secondApproved.ApprovedBy);
        Assert.Equal(("Synthetic review", TestData.Now, TestData.Now.AddDays(180), false), (secondApproved.ApprovalSource, secondApproved.ApprovedAt, secondApproved.ReviewDueAt, secondApproved.ReviewOverdue));
        Assert.Equal(SupportResourceStatus.Retired, firstNow.Status);
        Assert.Equal(TestData.Now, firstNow.RetiredAt);
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.retired", first.Id, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.approved", first.Id, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.approved", second.Id, cancellationToken));
        Assert.Equal(0, await AuditCountAsync("support_resources.resource.retired", second.Id, cancellationToken));
    }

    [Fact]
    public async Task Approving_without_arabic_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var draft = await DraftedAsync(admin, TestData.UniqueCode("resource"), cancellationToken, arabic: false);

        using var response = await admin.PostJsonAsync($"{Route}/{draft.Id}/approve", Approval(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("support_resources.arabic_text_missing", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(SupportResourceStatus.Draft, (await GetAsync(admin, draft.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task A_failed_approval_leaves_the_previous_version_approved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var code = TestData.UniqueCode("resource");
        using var admin = factory.AsSystemAdministrator();
        var first = await DraftedAsync(admin, code, cancellationToken);
        await ApprovedAsync(admin, first.Id, cancellationToken);
        var second = await DraftedAsync(admin, code, cancellationToken, arabic: false);

        using var response = await admin.PostJsonAsync($"{Route}/{second.Id}/approve", Approval(), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SupportResourceStatus.Approved, (await GetAsync(admin, first.Id, cancellationToken)).Status);
        Assert.Equal(0, await AuditCountAsync("support_resources.resource.retired", first.Id, cancellationToken));
    }

    [Fact]
    public async Task Approving_with_a_missing_source_returns_400_keyed_by_the_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var draft = await DraftedAsync(admin, TestData.UniqueCode("resource"), cancellationToken);

        using var response = await admin.PostJsonAsync($"{Route}/{draft.Id}/approve", Approval(source: null), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("approvalSource", await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(367)]
    public async Task Approving_with_a_bad_source_or_review_date_returns_400(int days)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var draft = await DraftedAsync(admin, TestData.UniqueCode("resource"), cancellationToken);

        using var response = await admin.PostJsonAsync(
            $"{Route}/{draft.Id}/approve", Approval(TestData.Now.AddDays(days)), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("support_resources.review_due_invalid", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Retiring_needs_an_approved_resource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var draft = await DraftedAsync(admin, TestData.UniqueCode("resource"), cancellationToken);

        using var refused = await admin.PostAsync($"{Route}/{draft.Id}/retire", null, cancellationToken);
        await ApprovedAsync(admin, draft.Id, cancellationToken);
        using var retired = await admin.PostAsync($"{Route}/{draft.Id}/retire", null, cancellationToken);
        var body = await retired.ReadAsync<SupportResourceResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("support_resources.not_approved", await refused.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        Assert.Equal((SupportResourceStatus.Retired, (DateTimeOffset?)TestData.Now), (body.Status, body.RetiredAt));
        Assert.Equal(1, await AuditCountAsync("support_resources.resource.retired", draft.Id, cancellationToken));
    }

    [Fact]
    public async Task An_unknown_resource_returns_404_on_every_route()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();
        var url = $"{Route}/{Guid.NewGuid()}";

        var responses = new[]
        {
            await admin.GetAsync(url, cancellationToken),
            await admin.PutJsonAsync(url, Draft("ignored"), cancellationToken),
            await admin.PostJsonAsync($"{url}/approve", Approval(), cancellationToken),
            await admin.PostAsync($"{url}/retire", null, cancellationToken),
        };

        foreach (var response in responses)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("support_resources.not_found", await response.ProblemCodeAsync(cancellationToken));
            }
        }
    }

    [Fact]
    public async Task The_list_filters_by_status_and_pages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var prefix = TestData.UniqueCode("resource");
        var codeA = prefix + "_a";
        var codeB = prefix + "_b";
        using var admin = factory.AsSystemAdministrator();
        var a1 = await DraftedAsync(admin, codeA, cancellationToken);
        var a2 = await DraftedAsync(admin, codeA, cancellationToken);
        var b1 = await DraftedAsync(admin, codeB, cancellationToken);
        await ApprovedAsync(admin, b1.Id, cancellationToken);
        HashSet<Guid> mine = [a1.Id, a2.Id, b1.Id];

        async Task<PagedResponse<SupportResourceResponse>> ListAsync(string query)
        {
            using var response = await admin.GetAsync($"{Route}?{query}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadAsync<PagedResponse<SupportResourceResponse>>(cancellationToken);
        }

        // Reads every page, so the test holds however many rows other tests have left in the shared database. The
        // collection runs one test at a time, so no row arrives between pages.
        async Task<List<SupportResourceResponse>> EveryPageAsync(string filter)
        {
            const int pageSize = 10;
            var items = new List<SupportResourceResponse>();
            for (var number = 1; ; number++)
            {
                var page = await ListAsync($"{filter}page={number}&pageSize={pageSize}");
                Assert.Equal((number, pageSize), (page.Page, page.PageSize));
                items.AddRange(page.Items);
                if (page.Items.Count < pageSize)
                {
                    Assert.Equal(page.TotalCount, items.Count);
                    return items;
                }
            }
        }

        static List<(string Code, int Version)> Mine(IEnumerable<SupportResourceResponse> items, HashSet<Guid> ids) =>
            items.Where(item => ids.Contains(item.Id)).Select(item => (item.Code, item.VersionNumber)).ToList();

        var all = await EveryPageAsync(string.Empty);
        var drafts = await EveryPageAsync("status=draft&");
        var approved = await EveryPageAsync("status=approved&");
        var firstPage = await ListAsync("pageSize=1");
        var secondPage = await ListAsync("pageSize=1&page=2");

        Assert.Equal([(codeA, 2), (codeA, 1), (codeB, 1)], Mine(all, mine));
        Assert.Equal(all.Count, all.Select(item => item.Id).Distinct().Count());
        Assert.Equal([(codeA, 2), (codeA, 1)], Mine(drafts, mine));
        Assert.All(drafts, item => Assert.Equal(SupportResourceStatus.Draft, item.Status));
        Assert.Equal([(codeB, 1)], Mine(approved, mine));
        Assert.All(approved, item => Assert.Equal(SupportResourceStatus.Approved, item.Status));
        Assert.Equal(all.Count, firstPage.TotalCount);
        Assert.Equal((1, 1), (firstPage.Page, firstPage.PageSize));
        Assert.Equal(all[0].Id, Assert.Single(firstPage.Items).Id);
        Assert.Equal(all[1].Id, Assert.Single(secondPage.Items).Id);
    }

    [Fact]
    public async Task The_list_rejects_a_bad_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.AsSystemAdministrator();

        using var response = await admin.GetAsync($"{Route}?status=nonsense", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Participant)]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SupportCoordinator)]
    public async Task Only_system_administrators_manage_the_directory(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.As(ApiClients.NewSubject(role), role);

        using var created = await client.PostJsonAsync(Route, Draft(TestData.UniqueCode("resource")), cancellationToken);
        using var listed = await client.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.As(null);

        using var created = await client.PostJsonAsync(Route, Draft(TestData.UniqueCode("resource")), cancellationToken);
        using var listed = await client.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
    }
}
