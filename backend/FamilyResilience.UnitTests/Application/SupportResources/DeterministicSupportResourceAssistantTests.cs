using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.UnitTests.Application.SupportResources;

public sealed class DeterministicSupportResourceAssistantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static SupportResource Resource()
    {
        var content = SupportResourceContent.Create(
            "مورد تجريبي", "Synthetic resource", "ملخص تجريبي", "Synthetic summary", "جهة تجريبية", "Synthetic provider",
            "https://example.org/synthetic", ["synthetic_support"], [], ["ar", "en"], SupportResourceVisibility.Everyone);
        return SupportResource.CreateDraft("synthetic_code", 1, content.Value, Now).Value;
    }

    private static SupportResourceQuery Query(string? text, string[]? categories = null, int limit = 5) =>
        new(text, SupportResourceQueryText.Parse(text), categories ?? [], [], [SupportResourceVisibility.Everyone], limit);

    [Fact]
    public async Task Without_terms_or_categories_the_search_is_not_called()
    {
        var search = new FakeSupportResourceSearch(Resource());
        var assistant = new DeterministicSupportResourceAssistant(search, new FixedTimeProvider(Now));

        var answer = await assistant.DiscoverAsync(Query("!!! 123"), TestContext.Current.CancellationToken);

        Assert.Empty(search.Searches);
        Assert.Empty(answer.Matches);
        Assert.Equal(DiscoveryOutcome.NoMatch, answer.Outcome);
    }

    [Fact]
    public async Task Text_searches_use_half_coverage_and_the_limit()
    {
        var search = new FakeSupportResourceSearch(Resource());
        var assistant = new DeterministicSupportResourceAssistant(search, new FixedTimeProvider(Now));
        var query = Query("debt advice", ["synthetic_support"], limit: 3);

        await assistant.DiscoverAsync(query, TestContext.Current.CancellationToken);

        var criteria = Assert.Single(search.Searches);
        Assert.Equal((TermCoverage.Half, 3, Now), (criteria.Coverage, criteria.Limit, criteria.Now));
        Assert.Equal(query.Terms, criteria.Terms);
        Assert.Equal(["synthetic_support"], criteria.Categories);
        Assert.Equal([SupportResourceVisibility.Everyone], criteria.Visibilities);
    }

    [Fact]
    public async Task The_answer_is_deterministic_without_a_fallback()
    {
        var resource = Resource();
        var assistant = new DeterministicSupportResourceAssistant(new FakeSupportResourceSearch(resource), new FixedTimeProvider(Now));

        var answer = await assistant.DiscoverAsync(Query("debt"), TestContext.Current.CancellationToken);

        Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)null, DiscoveryOutcome.Matched), (answer.Mode, answer.Fallback, answer.Outcome));
        Assert.Same(resource, Assert.Single(answer.Matches));
    }
}
