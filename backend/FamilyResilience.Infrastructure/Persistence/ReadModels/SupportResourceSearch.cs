using System.Text.RegularExpressions;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The deterministic directory search (spec §6.2). One PostgreSQL full-text statement filters by access first, then
/// counts and ranks term matches and returns resource IDs; the aggregates are then loaded untracked and returned in
/// that order. The SQL text is constant: term texts, configurations, filters, the time and the limit are all
/// parameters, and nothing from a request is ever spliced into it.
/// </summary>
public sealed partial class SupportResourceSearch(FamilyResilienceDbContext dbContext) : ISupportResourceSearch
{
    private const string EnglishConfiguration = "english";
    private const string ArabicConfiguration = "simple";

    public async Task<IReadOnlyList<SupportResource>> SearchAsync(
        SupportResourceSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(criteria.Terms);
        ArgumentNullException.ThrowIfNull(criteria.Categories);
        ArgumentNullException.ThrowIfNull(criteria.Audiences);
        ArgumentNullException.ThrowIfNull(criteria.Visibilities);
        ArgumentOutOfRangeException.ThrowIfLessThan(criteria.Limit, 1);

        var (words, configurations) = TermQueries(criteria.Terms);
        var half = criteria.Coverage switch
        {
            TermCoverage.Half => true,
            TermCoverage.AnyTerm => false,
            _ => throw new ArgumentException("Unknown term coverage.", nameof(criteria)),
        };
        var categories = criteria.Categories.ToArray();
        var audiences = criteria.Audiences.ToArray();
        var visibilities = criteria.Visibilities.Select(visibility => visibility.ToString()).ToArray();
        var browse = categories.Length > 0;
        var now = criteria.Now.ToUniversalTime();
        var limit = criteria.Limit;

        // usable: one query per term, minus those PostgreSQL reduces to nothing (English stop words).
        // hits: every filtered row with how many usable terms it contains and the sum of their cover-density ranks.
        // Without usable terms every filtered row has 0 and 0, so a category browse falls back to code order.
        var ids = await dbContext.Database.SqlQuery<Guid>(
                $"""
                 WITH terms AS (
                     SELECT to_tsquery(term.config::regconfig, term.word) AS q
                     FROM unnest({words}::text[], {configurations}::text[]) AS term(word, config)
                 ),
                 usable AS (
                     SELECT q FROM terms WHERE numnode(q) > 0
                 ),
                 filtered AS (
                     SELECT r.id, r.code, r.version_number, r.search_vector
                     FROM support_resources AS r
                     WHERE r.status = 'Approved'
                       AND r.review_due_at > {now}
                       AND r.visibility = ANY({visibilities}::text[])
                       AND (cardinality({categories}::text[]) = 0 OR r.categories && {categories}::varchar[])
                       AND (cardinality({audiences}::text[]) = 0 OR r.audiences && {audiences}::varchar[])
                 ),
                 hits AS (
                     SELECT f.id, f.code, f.version_number,
                            count(u.q) AS matched,
                            coalesce(sum(ts_rank_cd(f.search_vector, u.q)), 0) AS score
                     FROM filtered AS f
                     LEFT JOIN usable AS u ON f.search_vector @@ u.q
                     GROUP BY f.id, f.code, f.version_number
                 )
                 SELECT h.id AS "Value"
                 FROM hits AS h
                 CROSS JOIN (SELECT count(*) AS total FROM usable) AS n
                 WHERE CASE
                         WHEN n.total = 0 THEN {browse}
                         WHEN {half} THEN h.matched >= greatest(1, ceil(n.total * 0.5))
                         ELSE h.matched >= 1
                       END
                 ORDER BY h.matched DESC, h.score DESC, h.code, h.version_number DESC, h.id
                 LIMIT {limit}
                 """)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        // The same filter again, so a resource retired between the two statements is left out (fail closed).
        var resourceIds = ids.Select(SupportResourceId.From).ToArray();
        var resources = await Discoverable(criteria.Visibilities, now)
            .Where(resource => resourceIds.Contains(resource.Id))
            .ToDictionaryAsync(resource => resource.Id, cancellationToken);
        return resourceIds.Where(resources.ContainsKey).Select(id => resources[id]).ToList();
    }

    public async Task<IReadOnlyList<SupportResource>> ListAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibilities);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        // One approved version per code (ux_support_resources_one_approved_per_code); the version number only keeps
        // the order total.
        return await Discoverable(visibilities, now.ToUniversalTime())
            .OrderBy(resource => resource.Code)
            .ThenBy(resource => resource.VersionNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(
        IReadOnlyCollection<SupportResourceVisibility> visibilities, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibilities);
        return Discoverable(visibilities, now.ToUniversalTime()).CountAsync(cancellationToken);
    }

    public Task<SupportResource?> GetDiscoverableAsync(
        SupportResourceId id,
        IReadOnlyCollection<SupportResourceVisibility> visibilities,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibilities);
        return Discoverable(visibilities, now.ToUniversalTime())
            .SingleOrDefaultAsync(resource => resource.Id == id, cancellationToken);
    }

    /// <summary>
    /// The text and configuration of each term's <c>to_tsquery</c>. Callers pass terms from
    /// <see cref="SupportResourceQueryText.Parse"/>, which are letters only; anything else is refused here, so no
    /// query operator or quote can reach PostgreSQL. The message never repeats the term.
    /// </summary>
    private static (string[] Words, string[] Configurations) TermQueries(IReadOnlyList<SearchTerm> terms)
    {
        var words = new string[terms.Count];
        var configurations = new string[terms.Count];
        for (var index = 0; index < terms.Count; index++)
        {
            var term = terms[index];
            if (term?.Text is not { } text || !Letters().IsMatch(text))
            {
                throw new ArgumentException("Search terms must be 1 to 64 letters.", nameof(terms));
            }

            (words[index], configurations[index]) = term.Kind switch
            {
                SearchTermKind.English => (text, EnglishConfiguration),
                SearchTermKind.Arabic => (term.IsPrefix ? text + ":*" : text, ArabicConfiguration),
                _ => throw new ArgumentException("Unknown search term kind.", nameof(terms)),
            };
        }

        return (words, configurations);
    }

    private IQueryable<SupportResource> Discoverable(
        IReadOnlyCollection<SupportResourceVisibility> visibilities, DateTimeOffset now)
    {
        var allowed = visibilities.ToArray();
        return dbContext.SupportResources.AsNoTracking()
            .Where(resource => resource.Status == SupportResourceStatus.Approved
                && resource.ReviewDueAt > now
                && allowed.Contains(resource.Visibility));
    }

    [GeneratedRegex(@"^\p{L}{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Letters();
}
