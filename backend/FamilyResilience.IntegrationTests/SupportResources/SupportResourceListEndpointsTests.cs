using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The directory listing for the app's All services place. The shared database holds other tests' resources and the
/// tests in a collection run one at a time, so each test compares the whole listing with what it added, never with a
/// fixed count. Every entry is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportResourceListEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/v1/support-resources";

    private static SupportResource Resource(
        string code,
        SupportResourceVisibility visibility = SupportResourceVisibility.Everyone,
        bool approved = true,
        bool overdue = false)
    {
        var content = TestData.Ok(SupportResourceContent.Create(
            "مورد تجريبي", $"Synthetic {code}", "ملخص تجريبي", "Synthetic summary", "جهة تجريبية", "Synthetic provider",
            "https://example.org/synthetic", ["synthetic_listing"], [], ["ar", "en"], visibility));
        var resource = TestData.Ok(SupportResource.CreateDraft(code, 1, content, TestData.Now.AddDays(-60)));
        if (approved)
        {
            // An overdue entry was approved earlier with a review date that has now passed.
            var approval = overdue
                ? TestData.Ok(Approval.Create("approver-subject", TestData.Now.AddDays(-30), "Synthetic test approval"))
                : TestData.TestApproval();
            TestData.Ok(resource.Approve(approval, overdue ? TestData.Now.AddDays(-1) : TestData.Now.AddDays(90), approval.ApprovedAt));
        }

        return resource;
    }

    /// <summary>Adds the resources, named by codes sharing one new prefix, and returns that prefix.</summary>
    private async Task<(string Prefix, Dictionary<string, SupportResource> Added)> AddAsync(CancellationToken cancellationToken)
    {
        var prefix = TestData.UniqueCode("listing");
        var added = new Dictionary<string, SupportResource>
        {
            ["public_b"] = Resource($"{prefix}_b"),
            ["public_a"] = Resource($"{prefix}_a"),
            ["public_c"] = Resource($"{prefix}_c"),
            ["staff"] = Resource($"{prefix}_d", SupportResourceVisibility.StaffOnly),
            ["draft"] = Resource($"{prefix}_e", approved: false),
            ["overdue"] = Resource($"{prefix}_f", overdue: true),
        };
        await factory.InScopeAsync(
            services =>
            {
                var repository = services.GetRequiredService<ISupportResourceRepository>();
                foreach (var resource in added.Values)
                {
                    repository.Add(resource);
                }

                return Task.CompletedTask;
            },
            cancellationToken);
        return (prefix, added);
    }

    /// <summary>Every page of the listing, the largest page size at a time.</summary>
    private static async Task<(List<PublishedSupportResourceResponse> Items, int Total)> EverythingAsync(
        HttpClient client, CancellationToken cancellationToken)
    {
        var items = new List<PublishedSupportResourceResponse>();
        for (var page = 1; ; page++)
        {
            using var response = await client.GetAsync($"{Route}?page={page}&pageSize=100", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.ReadAsync<PagedResponse<PublishedSupportResourceResponse>>(cancellationToken);
            items.AddRange(body.Items);
            if (body.Items.Count == 0 || items.Count >= body.TotalCount)
            {
                return (items, body.TotalCount);
            }
        }
    }

    [Fact]
    public async Task A_participant_lists_approved_in_date_public_entries_ordered_by_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (prefix, added) = await AddAsync(cancellationToken);
        using var participant = factory.AsParticipant();

        var (items, total) = await EverythingAsync(participant, cancellationToken);

        Assert.Equal(total, items.Count);
        Assert.Equal(
            [$"{prefix}_a", $"{prefix}_b", $"{prefix}_c"],
            items.Where(item => item.Code.StartsWith(prefix, StringComparison.Ordinal)).Select(item => item.Code));
        Assert.Equal(items.Count, items.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        var first = items.Single(item => item.Id == added["public_a"].Id.Value);
        Assert.Equal(
            ($"Synthetic {prefix}_a", "مورد تجريبي", "Synthetic provider", "https://example.org/synthetic", 1),
            (first.NameEn, first.NameAr, first.ProviderNameEn, first.Url, first.VersionNumber));
        Assert.Equal(["synthetic_listing"], first.Categories);
    }

    [Theory]
    [InlineData(Roles.SupportCoordinator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_also_see_staff_only_entries_but_never_drafts_or_overdue_ones(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (prefix, _) = await AddAsync(cancellationToken);
        using var staff = factory.As(ApiClients.NewSubject("staff"), role);

        var (items, _) = await EverythingAsync(staff, cancellationToken);

        Assert.Equal(
            [$"{prefix}_a", $"{prefix}_b", $"{prefix}_c", $"{prefix}_d"],
            items.Where(item => item.Code.StartsWith(prefix, StringComparison.Ordinal)).Select(item => item.Code));
    }

    [Fact]
    public async Task Pages_follow_the_same_order_and_carry_the_total()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await AddAsync(cancellationToken);
        using var participant = factory.AsParticipant();
        var (items, total) = await EverythingAsync(participant, cancellationToken);

        using var second = await participant.GetAsync($"{Route}?page=2&pageSize=2", cancellationToken);
        using var beyond = await participant.GetAsync($"{Route}?page={total + 1}&pageSize=1", cancellationToken);
        using var defaults = await participant.GetAsync(Route, cancellationToken);

        var page = await second.ReadAsync<PagedResponse<PublishedSupportResourceResponse>>(cancellationToken);
        Assert.Equal((2, 2, total), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal(items.Skip(2).Take(2).Select(item => item.Id), page.Items.Select(item => item.Id));
        Assert.Empty((await beyond.ReadAsync<PagedResponse<PublishedSupportResourceResponse>>(cancellationToken)).Items);
        var first = await defaults.ReadAsync<PagedResponse<PublishedSupportResourceResponse>>(cancellationToken);
        Assert.Equal((1, 20), (first.Page, first.PageSize));
        Assert.Equal(items.Take(20).Select(item => item.Id), first.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task An_invalid_page_is_400(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.GetAsync($"{Route}?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEmpty(await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_roles_outside_the_directory_get_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = factory.As(null);
        using var researcher = factory.AsResearcher();
        using var programAdministrator = factory.AsProgramAdministrator();

        using var unauthenticated = await anonymous.GetAsync(Route, cancellationToken);
        using var forbidden = await researcher.GetAsync(Route, cancellationToken);
        using var alsoForbidden = await programAdministrator.GetAsync(Route, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, alsoForbidden.StatusCode);
    }
}
