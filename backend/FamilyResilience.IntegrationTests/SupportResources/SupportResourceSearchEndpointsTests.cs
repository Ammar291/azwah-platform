using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The reader API: participants and staff search the approved directory and open one entry. The shared database holds
/// other tests' resources, so each test uses its own category and its own made-up word. Every entry is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportResourceSearchEndpointsTests(ApiFactory factory)
{
    private const string AdminRoute = "/api/v1/admin/support-resources";
    private const string Route = "/api/v1/support-resources";

    /// <summary>A letters-only word no other test uses, so it survives query parsing as one term.</summary>
    private static string UniqueWord() =>
        string.Concat(Guid.NewGuid().ToString("N").Select(c => (char)(c is >= '0' and <= '9' ? 'k' + (c - '0') : c)));

    private static string UniqueCategory() => TestData.UniqueCode("synthetic_cat");

    private static object Draft(string category, string word, string visibility = "everyone") => new
    {
        code = TestData.UniqueCode("resource"),
        nameAr = "مورد تجريبي",
        nameEn = $"Synthetic {word}",
        summaryAr = "ملخص تجريبي",
        summaryEn = $"Synthetic summary about {word}",
        providerNameAr = "جهة تجريبية",
        providerNameEn = "Synthetic provider",
        url = $"https://example.org/{word}",
        categories = new[] { category },
        audiences = new[] { "adults" },
        serviceLanguages = new[] { "ar", "en" },
        visibility,
    };

    private async Task<SupportResourceResponse> DraftedAsync(
        string category, string word, CancellationToken cancellationToken, string visibility = "everyone")
    {
        using var admin = factory.AsSystemAdministrator();
        using var response = await admin.PostJsonAsync(AdminRoute, Draft(category, word, visibility), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private async Task<SupportResourceResponse> ApprovedAsync(
        string category, string word, CancellationToken cancellationToken, string visibility = "everyone")
    {
        var draft = await DraftedAsync(category, word, cancellationToken, visibility);
        using var admin = factory.AsSystemAdministrator();
        using var response = await admin.PostJsonAsync(
            $"{AdminRoute}/{draft.Id}/approve",
            new { approvalSource = "Synthetic review", reviewDueAt = TestData.Now.AddDays(180) },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceResponse>(cancellationToken);
    }

    private static async Task<SupportResourceDiscoveryResponse> SearchOkAsync(
        HttpClient client, object body, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync($"{Route}/search", body, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceDiscoveryResponse>(cancellationToken);
    }

    [Fact]
    public async Task A_participant_finds_an_approved_resource_with_its_source_link()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var resource = await ApprovedAsync(category, word, cancellationToken);
        using var participant = factory.AsParticipant();

        var found = await SearchOkAsync(participant, new { text = word, categories = new[] { category } }, cancellationToken);

        Assert.Equal(
            (DiscoveryOutcome.Matched, DiscoveryMode.Deterministic, (DiscoveryFallback?)null, DiscoveryNotice.InformationOnly),
            (found.Outcome, found.Mode, found.Fallback, found.Notice));
        var match = Assert.Single(found.Matches);
        Assert.Equal((resource.Id, resource.Url, 1, resource.Code, resource.VersionNumber), (match.Id, match.Url, match.Rank, match.Code, match.VersionNumber));
        Assert.Equal(($"Synthetic {word}", "مورد تجريبي", "Synthetic provider", "جهة تجريبية"), (match.NameEn, match.NameAr, match.ProviderNameEn, match.ProviderNameAr));
        Assert.Equal([category], match.Categories);
        Assert.Equal(["adults"], match.Audiences);
        Assert.Equal(["ar", "en"], match.ServiceLanguages);
    }

    [Fact]
    public async Task The_response_uses_snake_case_codes_on_the_wire()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.PostJsonAsync(
            $"{Route}/search", new { text = UniqueWord(), categories = new[] { UniqueCategory() } }, cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"outcome\":\"no_match\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"deterministic\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"fallback\":null", raw, StringComparison.Ordinal);
        Assert.Contains("\"notice\":\"information_only\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_search_with_nothing_relevant_says_no_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = UniqueCategory();
        await ApprovedAsync(category, UniqueWord(), cancellationToken);
        using var participant = factory.AsParticipant();

        var found = await SearchOkAsync(participant, new { text = UniqueWord(), categories = new[] { category } }, cancellationToken);

        Assert.Equal((DiscoveryOutcome.NoMatch, DiscoveryMode.Deterministic), (found.Outcome, found.Mode));
        Assert.Empty(found.Matches);
    }

    [Fact]
    public async Task Text_without_usable_words_is_a_no_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        var found = await SearchOkAsync(participant, new { text = "!!! 123" }, cancellationToken);

        Assert.Equal(DiscoveryOutcome.NoMatch, found.Outcome);
        Assert.Empty(found.Matches);
    }

    [Fact]
    public async Task A_category_only_search_browses_by_code_up_to_the_limit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = UniqueCategory();
        var resources = new List<SupportResourceResponse>();
        for (var i = 0; i < 3; i++)
        {
            resources.Add(await ApprovedAsync(category, UniqueWord(), cancellationToken));
        }

        using var participant = factory.AsParticipant();

        var found = await SearchOkAsync(participant, new { categories = new[] { category }, limit = 2 }, cancellationToken);

        Assert.Equal(DiscoveryOutcome.Matched, found.Outcome);
        Assert.Equal(resources.Select(resource => resource.Code).Order().Take(2), found.Matches.Select(match => match.Code));
        Assert.Equal([1, 2], found.Matches.Select(match => match.Rank));
    }

    [Fact]
    public async Task Participants_never_see_staff_only_resources_but_coordinators_do()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (category, word) = (UniqueCategory(), UniqueWord());
        var staffOnly = await ApprovedAsync(category, word, cancellationToken, "staff_only");
        var body = new { text = word, categories = new[] { category } };
        using var participant = factory.AsParticipant();
        using var coordinator = factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        var seenByParticipant = await SearchOkAsync(participant, body, cancellationToken);
        var seenByCoordinator = await SearchOkAsync(coordinator, body, cancellationToken);

        Assert.Empty(seenByParticipant.Matches);
        Assert.Equal(staffOnly.Id, Assert.Single(seenByCoordinator.Matches).Id);
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    public async Task Researchers_and_program_administrators_cannot_search(string role)
    {
        using var client = factory.As(ApiClients.NewSubject("staff"), role);

        using var search = await client.PostJsonAsync($"{Route}/search", new { text = "debt" }, TestContext.Current.CancellationToken);
        using var get = await client.GetAsync($"{Route}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        using var client = factory.As(null);

        using var search = await client.PostJsonAsync($"{Route}/search", new { text = "debt" }, TestContext.Current.CancellationToken);
        using var get = await client.GetAsync($"{Route}/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, search.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    public static TheoryData<string, string> BadRequests => new()
    {
        { """{"text":"__LONG__"}""", "text" },
        { "{}", "text" },
        { """{"text":"debt","categories":["a","b","c","d","e","f"]}""", "categories" },
        { """{"text":"debt","categories":["Bad Code"]}""", "categories[0]" },
        { """{"text":"debt","audiences":["Bad Code"]}""", "audiences[0]" },
        { """{"text":"debt","limit":0}""", "limit" },
        { """{"text":"debt","limit":11}""", "limit" },
    };

    [Theory]
    [MemberData(nameof(BadRequests))]
    public async Task Bad_search_requests_return_400_keyed_by_the_field(string json, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var participant = factory.AsParticipant();

        using var response = await participant.PostRawJsonAsync(
            $"{Route}/search", json.Replace("__LONG__", new string('a', 201), StringComparison.Ordinal), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task The_published_resource_endpoint_shows_only_discoverable_visible_resources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = UniqueCategory();
        var approved = await ApprovedAsync(category, UniqueWord(), cancellationToken);
        var draft = await DraftedAsync(category, UniqueWord(), cancellationToken);
        var retired = await ApprovedAsync(category, UniqueWord(), cancellationToken);
        var staffOnly = await ApprovedAsync(category, UniqueWord(), cancellationToken, "staff_only");
        using (var admin = factory.AsSystemAdministrator())
        {
            using var retirement = await admin.PostAsync($"{AdminRoute}/{retired.Id}/retire", null, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, retirement.StatusCode);
        }

        using var participant = factory.AsParticipant();
        using var coordinator = factory.As(ApiClients.NewSubject("coordinator"), Roles.SupportCoordinator);

        using var ok = await participant.GetAsync($"{Route}/{approved.Id}", cancellationToken);
        var shown = await ok.ReadAsync<PublishedSupportResourceResponse>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((approved.Id, approved.Url, approved.Code), (shown.Id, shown.Url, shown.Code));
        foreach (var id in new[] { draft.Id, retired.Id, Guid.NewGuid(), staffOnly.Id })
        {
            using var missing = await participant.GetAsync($"{Route}/{id}", cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("support_resources.not_found", await missing.ProblemCodeAsync(cancellationToken));
        }

        using var forCoordinator = await coordinator.GetAsync($"{Route}/{staffOnly.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, forCoordinator.StatusCode);
    }

    [Fact]
    public async Task Search_text_is_never_logged_or_counted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        // Every level, so the request's own log entries are captured and the checks below have something to inspect.
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(
            logging => logging.AddProvider(logs).AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace)));
        using var measurements = new SupportResourceMeasurements(host);
        using var participant = host.CreateClient();
        participant.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("participant"));
        participant.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Participant);
        var category = UniqueCategory();
        var word = UniqueWord();
        await ApprovedAsync(category, word, cancellationToken);
        var startup = logs.Entries.Count;

        var text = $"zqxmarker {word} 0501234567";
        var found = await SearchOkAsync(participant, new { text, categories = new[] { category } }, cancellationToken);

        Assert.Equal(DiscoveryOutcome.Matched, found.Outcome);
        var request = logs.Entries.Skip(startup).ToList();
        Assert.NotEmpty(request);
        Assert.Contains(request, entry => entry.Category.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal));
        Assert.DoesNotContain(request, entry => entry.Message.Contains("zqxmarker", StringComparison.OrdinalIgnoreCase)
            || entry.Message.Contains("0501234567", StringComparison.Ordinal) || entry.Message.Contains(word, StringComparison.OrdinalIgnoreCase));
        var recorded = measurements.All;
        Assert.NotEmpty(recorded);
        Assert.All(recorded.SelectMany(m => m.Tags.Values), value =>
        {
            Assert.DoesNotContain("zqxmarker", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("0501234567", value, StringComparison.Ordinal);
            Assert.DoesNotContain(word, value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(category, value, StringComparison.Ordinal);
        });
        Assert.Equal(1, measurements.Total(
            "azwah.support_resources.searches", ("mode", "deterministic"), ("outcome", "matched"), ("fallback", "none")));
    }

    [Fact]
    public async Task Searches_are_not_audited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("coordinator");
        using var coordinator = factory.As(subject, Roles.SupportCoordinator);

        await SearchOkAsync(coordinator, new { text = "debt advice" }, cancellationToken);
        using var get = await coordinator.GetAsync($"{Route}/{Guid.NewGuid()}", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        var audited = await factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.ActorId == subject && audit.Action.StartsWith("support_resources."),
            cancellationToken));
        Assert.Equal(0, audited);
    }
}
