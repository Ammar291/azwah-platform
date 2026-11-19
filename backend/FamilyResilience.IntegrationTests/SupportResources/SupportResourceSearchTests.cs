using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.SupportResources;

/// <summary>
/// The deterministic directory search against PostgreSQL full-text search: access filters first, then term coverage
/// and a stable rank. The shared database holds other tests' resources, so every search here is limited to a category
/// no other test uses. Every entry is synthetic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportResourceSearchTests(ApiFactory factory)
{
    private static readonly DateTimeOffset ReviewDueAt = TestData.Now.AddDays(100);

    private static SupportResourceContent Content(
        string category,
        string nameEn = "Synthetic resource",
        string summaryEn = "Synthetic summary",
        string summaryAr = "ملخص تجريبي",
        SupportResourceVisibility visibility = SupportResourceVisibility.Everyone,
        string? audience = null) =>
        TestData.Ok(SupportResourceContent.Create(
            "مورد تجريبي", nameEn, summaryAr, summaryEn, "جهة تجريبية", "Synthetic provider",
            "https://example.org/synthetic", [category], audience is null ? [] : [audience], ["ar", "en"], visibility));

    private static SupportResource Draft(string code, SupportResourceContent content) =>
        TestData.Ok(SupportResource.CreateDraft(code, 1, content, TestData.Now));

    private static SupportResource Approved(string code, SupportResourceContent content)
    {
        var resource = Draft(code, content);
        TestData.Ok(resource.Approve(TestData.TestApproval(), ReviewDueAt, TestData.Now));
        return resource;
    }

    private static SupportResourceSearchCriteria Criteria(
        string text,
        IReadOnlyList<string> categories,
        IReadOnlyList<string>? audiences = null,
        IReadOnlyCollection<SupportResourceVisibility>? visibilities = null,
        DateTimeOffset? now = null,
        TermCoverage coverage = TermCoverage.Half,
        int limit = 10) =>
        new(
            SupportResourceQueryText.Parse(text),
            categories,
            audiences ?? [],
            visibilities ?? [SupportResourceVisibility.Everyone],
            now ?? TestData.Now,
            coverage,
            limit);

    private static List<SupportResourceId> Ids(IEnumerable<SupportResource> resources) =>
        resources.Select(resource => resource.Id).ToList();

    private Task SaveAsync(IEnumerable<SupportResource> resources, CancellationToken cancellationToken) =>
        factory.InScopeAsync(
            services =>
            {
                var repository = services.GetRequiredService<ISupportResourceRepository>();
                foreach (var resource in resources)
                {
                    repository.Add(resource);
                }

                return Task.CompletedTask;
            },
            cancellationToken);

    private async Task<IReadOnlyList<SupportResource>> SearchAsync(
        SupportResourceSearchCriteria criteria, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var found = await scope.ServiceProvider.GetRequiredService<ISupportResourceSearch>()
            .SearchAsync(criteria, cancellationToken);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>().ChangeTracker.Entries());
        return found;
    }

    private async Task<SupportResource?> GetAsync(
        SupportResourceId id,
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var found = await scope.ServiceProvider.GetRequiredService<ISupportResourceSearch>()
            .GetDiscoverableAsync(id, visibilities, now, cancellationToken);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>().ChangeTracker.Entries());
        return found;
    }

    [Fact]
    public async Task Only_approved_resources_before_their_review_date_are_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var draft = Draft(TestData.UniqueCode("res"), Content(category, summaryEn: "Synthetic debt advice"));
        var retired = Approved(TestData.UniqueCode("res"), Content(category, summaryEn: "Synthetic debt advice"));
        TestData.Ok(retired.Retire(TestData.Now));
        var approved = Approved(TestData.UniqueCode("res"), Content(category, summaryEn: "Synthetic debt advice"));
        await SaveAsync([draft, retired, approved], cancellationToken);

        var current = await SearchAsync(Criteria("debt", [category]), cancellationToken);
        var atReview = await SearchAsync(Criteria("debt", [category], now: ReviewDueAt), cancellationToken);
        var afterReview = await SearchAsync(Criteria("debt", [category], now: ReviewDueAt.AddSeconds(1)), cancellationToken);

        var found = Assert.Single(current);
        Assert.Equal((approved.Id, approved.Code, SupportResourceStatus.Approved), (found.Id, found.Code, found.Status));
        Assert.Equal(("Synthetic resource", "Synthetic debt advice"), (found.Name.English, found.Summary.English));
        Assert.Equal([category], found.Categories);
        Assert.Equal(ReviewDueAt, found.ReviewDueAt);
        Assert.Empty(atReview);
        Assert.Empty(afterReview);
    }

    [Fact]
    public async Task Staff_only_resources_need_staff_visibility()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var code = TestData.UniqueCode("res");
        var everyone = Approved($"{code}_a", Content(category, summaryEn: "Synthetic debt advice"));
        var staffOnly = Approved(
            $"{code}_b",
            Content(category, summaryEn: "Synthetic debt advice", visibility: SupportResourceVisibility.StaffOnly));
        await SaveAsync([everyone, staffOnly], cancellationToken);

        var participantView = await SearchAsync(Criteria("debt", [category]), cancellationToken);
        var staffView = await SearchAsync(
            Criteria("debt", [category], visibilities: [SupportResourceVisibility.Everyone, SupportResourceVisibility.StaffOnly]),
            cancellationToken);
        var nobody = await SearchAsync(Criteria("debt", [category], visibilities: []), cancellationToken);

        Assert.Equal([everyone.Id], Ids(participantView));
        Assert.Equal([everyone.Id, staffOnly.Id], Ids(staffView));
        Assert.Empty(nobody);
    }

    [Fact]
    public async Task Categories_and_audiences_filter_before_ranking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var otherCategory = TestData.UniqueCode("cat");
        var audience = TestData.UniqueCode("aud");
        var otherAudience = TestData.UniqueCode("aud");
        var code = TestData.UniqueCode("res");
        var weakMatch = Approved($"{code}_a", Content(category, summaryEn: "Synthetic debt advice", audience: audience));
        var otherCategoryNameMatch = Approved(
            $"{code}_b", Content(otherCategory, nameEn: "Synthetic debt service", audience: audience));
        var otherAudienceNameMatch = Approved(
            $"{code}_c", Content(category, nameEn: "Synthetic debt service", audience: otherAudience));
        await SaveAsync([weakMatch, otherCategoryNameMatch, otherAudienceNameMatch], cancellationToken);

        var filtered = await SearchAsync(Criteria("debt", [category], audiences: [audience], limit: 1), cancellationToken);
        var categoryOnly = await SearchAsync(Criteria("debt", [category], limit: 2), cancellationToken);
        var bothCategories = await SearchAsync(
            Criteria("debt", [otherCategory, category], audiences: [audience]), cancellationToken);

        Assert.Equal([weakMatch.Id], Ids(filtered));
        Assert.Equal([otherAudienceNameMatch.Id, weakMatch.Id], Ids(categoryOnly));
        Assert.Equal([otherCategoryNameMatch.Id, weakMatch.Id], Ids(bothCategories));
    }

    [Fact]
    public async Task A_resource_needs_half_of_the_terms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var code = TestData.UniqueCode("res");
        var oneTerm = Approved($"{code}_a", Content(category, summaryEn: "Synthetic debt advice"));
        var twoTerms = Approved($"{code}_b", Content(category, summaryEn: "Synthetic debt and housing advice"));
        await SaveAsync([oneTerm, twoTerms], cancellationToken);

        var half = await SearchAsync(Criteria("debt housing transport", [category]), cancellationToken);
        var anyTerm = await SearchAsync(
            Criteria("debt housing transport", [category], coverage: TermCoverage.AnyTerm), cancellationToken);

        // Stop words do not count towards the terms: one usable term out of three words still needs only one match.
        var withStopWords = await SearchAsync(Criteria("the debt of", [category]), cancellationToken);

        Assert.Equal([twoTerms.Id], Ids(half));
        Assert.Equal([twoTerms.Id, oneTerm.Id], Ids(anyTerm));
        Assert.Equal(new HashSet<SupportResourceId> { oneTerm.Id, twoTerms.Id }, Ids(withStopWords).ToHashSet());
    }

    [Fact]
    public async Task More_matched_terms_rank_first_then_name_weight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var code = TestData.UniqueCode("res");

        // Codes run against the expected order, so only the rank can produce it.
        var bothTermsInSummary = Approved($"{code}_c", Content(category, summaryEn: "Synthetic debt and rent advice"));
        var oneTermInName = Approved($"{code}_b", Content(category, nameEn: "Synthetic debt service"));
        var oneTermInSummary = Approved($"{code}_a", Content(category, summaryEn: "Synthetic debt advice"));
        await SaveAsync([oneTermInSummary, oneTermInName, bothTermsInSummary], cancellationToken);

        var found = await SearchAsync(Criteria("debt rent", [category]), cancellationToken);

        Assert.Equal([bothTermsInSummary.Id, oneTermInName.Id, oneTermInSummary.Id], Ids(found));
    }

    [Fact]
    public async Task Arabic_inflections_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var resource = Approved(TestData.UniqueCode("res"), Content(category, summaryAr: "برنامج لإدارة الديون"));

        // "Your debts": the document keeps its suffix, so only the query's prefix match finds it.
        var suffixed = Approved(TestData.UniqueCode("res"), Content(category, summaryAr: "تسوية ديونكم"));
        await SaveAsync([resource, suffixed], cancellationToken);

        var myDebts = await SearchAsync(Criteria("ديوني", [category]), cancellationToken);
        var andTheDebts = await SearchAsync(Criteria("والديون", [category]), cancellationToken);

        var both = new HashSet<SupportResourceId> { resource.Id, suffixed.Id };
        Assert.Equal(both, Ids(myDebts).ToHashSet());
        Assert.Equal(both, Ids(andTheDebts).ToHashSet());
    }

    [Fact]
    public async Task English_stems_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var resource = Approved(TestData.UniqueCode("res"), Content(category, summaryEn: "Debt management"));
        await SaveAsync([resource], cancellationToken);

        var found = await SearchAsync(Criteria("debts", [category]), cancellationToken);

        Assert.Equal([resource.Id], Ids(found));
    }

    [Fact]
    public async Task Stop_words_alone_with_a_category_browse_by_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var code = TestData.UniqueCode("res");
        var third = Approved($"{code}_c", Content(category, nameEn: "Synthetic debt service"));
        var first = Approved($"{code}_a", Content(category));
        var second = Approved($"{code}_b", Content(category, summaryEn: "Synthetic advice for the family"));
        await SaveAsync([third, first, second], cancellationToken);

        // Parsing drops English stop words now, so they are passed as terms directly: the statement must still ignore
        // whatever PostgreSQL reduces to nothing.
        IReadOnlyList<SearchTerm> stopWords =
            [new("the", SearchTermKind.English), new("and", SearchTermKind.English), new("of", SearchTermKind.English)];
        var browsed = await SearchAsync(Criteria("", [category]) with { Terms = stopWords }, cancellationToken);
        var withoutCategory = await SearchAsync(Criteria("", []) with { Terms = stopWords }, cancellationToken);

        Assert.Empty(SupportResourceQueryText.Parse("the and of"));
        Assert.Equal([first.Id, second.Id, third.Id], Ids(browsed));
        Assert.Empty(withoutCategory);
    }

    /// <summary>
    /// Parsing drops an English word exactly when PostgreSQL's <c>english</c> configuration would, so dropping stop words
    /// early never removes a word the search would have used. The sample is every word on the parser's own stop list
    /// (<see cref="EnglishStopWords.All"/>) plus ordinary words.
    /// </summary>
    [Fact]
    public async Task Parsing_drops_exactly_the_english_words_postgresql_drops()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] words =
        [
            .. EnglishStopWords.All.Order(StringComparer.Ordinal),
            "debt", "help", "want", "need", "family", "school", "parents", "would", "could", "also", "get", "please",
        ];
        Assert.Contains("the", words);

        var droppedByPostgres = await factory.QueryAsync(db => db.Database
            .SqlQuery<string>(
                $"""
                 SELECT w AS "Value" FROM unnest({words}::text[]) AS w
                 WHERE numnode(to_tsquery('english'::regconfig, w)) = 0
                 """)
            .ToListAsync(cancellationToken));

        Assert.Equal(
            droppedByPostgres.Order(StringComparer.Ordinal),
            words.Where(word => SupportResourceQueryText.Parse(word).Count == 0).Order(StringComparer.Ordinal));
        Assert.Contains("debt", words.Except(droppedByPostgres));
    }

    [Fact]
    public async Task Results_are_capped_and_stably_ordered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var code = TestData.UniqueCode("res");
        var resources = new[] { "d", "b", "e", "a", "c" }
            .Select(suffix => Approved($"{code}_{suffix}", Content(category, summaryEn: "Synthetic debt advice")))
            .ToList();
        await SaveAsync(resources, cancellationToken);

        var firstRun = await SearchAsync(Criteria("debt", [category], limit: 3), cancellationToken);
        var secondRun = await SearchAsync(Criteria("debt", [category], limit: 3), cancellationToken);

        var expected = resources.OrderBy(resource => resource.Code, StringComparer.Ordinal).Take(3).Select(resource => resource.Id);
        Assert.Equal(expected, Ids(firstRun));
        Assert.Equal(Ids(firstRun), Ids(secondRun));
    }

    [Theory]
    [InlineData("debt' OR 1=1")]
    [InlineData("debt:*")]
    [InlineData("debt rent")]
    [InlineData("debt\n")]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task A_term_that_is_not_letters_is_refused(string text)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var criteria = new SupportResourceSearchCriteria(
            [new SearchTerm(text, SearchTermKind.English)],
            [TestData.UniqueCode("cat")],
            [],
            [SupportResourceVisibility.Everyone],
            TestData.Now,
            TermCoverage.Half,
            5);

        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(criteria, cancellationToken));
    }

    [Fact]
    public async Task A_resource_is_got_by_id_only_while_discoverable_and_visible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var category = TestData.UniqueCode("cat");
        var everyone = Approved(TestData.UniqueCode("res"), Content(category));
        var staffOnly = Approved(
            TestData.UniqueCode("res"), Content(category, visibility: SupportResourceVisibility.StaffOnly));
        var draft = Draft(TestData.UniqueCode("res"), Content(category));
        var retired = Approved(TestData.UniqueCode("res"), Content(category));
        TestData.Ok(retired.Retire(TestData.Now));
        await SaveAsync([everyone, staffOnly, draft, retired], cancellationToken);
        SupportResourceVisibility[] participant = [SupportResourceVisibility.Everyone];
        SupportResourceVisibility[] staff = [SupportResourceVisibility.Everyone, SupportResourceVisibility.StaffOnly];

        Assert.Equal(everyone.Id, (await GetAsync(everyone.Id, participant, TestData.Now, cancellationToken))?.Id);
        Assert.Null(await GetAsync(everyone.Id, participant, ReviewDueAt, cancellationToken));
        Assert.Null(await GetAsync(staffOnly.Id, participant, TestData.Now, cancellationToken));
        Assert.Equal(staffOnly.Id, (await GetAsync(staffOnly.Id, staff, TestData.Now, cancellationToken))?.Id);
        Assert.Null(await GetAsync(draft.Id, staff, TestData.Now, cancellationToken));
        Assert.Null(await GetAsync(retired.Id, staff, TestData.Now, cancellationToken));
        Assert.Null(await GetAsync(SupportResourceId.New(), staff, TestData.Now, cancellationToken));
    }

    [Theory]
    [InlineData("الديون")]
    [InlineData("والديون")]
    [InlineData("بالأسرة")]
    [InlineData("للأطفال")]
    [InlineData("الى")]
    [InlineData("مُسَاعَدَة")]
    [InlineData("ٱلتثقيف")]
    [InlineData("إدارة")]
    [InlineData("أسرة")]
    [InlineData("آمنة")]
    [InlineData("الإرشاد")]
    [InlineData("مستشفى")]
    [InlineData("على")]
    [InlineData("مدرسة")]
    [InlineData("مـدرسـة")]
    [InlineData("قَرْضٌ")]
    [InlineData("هٰذا")]
    [InlineData("فالعمل")]
    [InlineData("كالبيت")]
    [InlineData("الم")]
    public async Task The_sql_normalisation_matches_the_csharp_normalisation(string word)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // EF Core passes raw SQL that has parameters through string.Format, so the expression's literal braces (the
        // regular expression's "{2,}") are doubled; the word itself travels only as the parameter.
        var sql = ("SELECT " + SupportResourceSearchSql.NormaliseArabic("@word") + " AS \"Value\"")
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);

        var normalised = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>(sql, new NpgsqlParameter("word", word))
            .ToListAsync(cancellationToken));

        Assert.Equal(SupportResourceQueryText.NormaliseArabic(word), Assert.Single(normalised));
    }
}
