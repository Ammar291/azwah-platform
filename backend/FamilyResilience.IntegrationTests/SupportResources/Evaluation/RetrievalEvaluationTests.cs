using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>
/// Measures the deterministic search on the synthetic evaluation set (spec §9) and holds it to regression floors. The
/// numbers describe a synthetic set, not a real directory. The report prints aggregates only, never query text. To see
/// the table, from <c>backend</c>: <c>dotnet test --project FamilyResilience.IntegrationTests --output detailed --
/// --filter-namespace "*SupportResources.Evaluation" --show-stdout All</c>.
/// </summary>
[Collection(SupportResourceEvaluationCollection.Name)]
public sealed class RetrievalEvaluationTests(ApiFactory factory)
{
    private const string SearchRoute = "/api/v1/support-resources/search";
    private const int K = 5;
    private const double Tolerance = 1e-9;

    // Regression floors, not targets: each is the value measured on 2026-10-18 (after the Task 9 query-text tuning)
    // rounded down to a multiple of 0.05; the false-positive ceiling is rounded up. The search is deterministic, so a
    // drop means the search or the set changed: look at why before moving a floor, and record the new measurement.
    // P@5 is low by construction: most queries have one or two relevant entries, so five results cap it at 0.2 or 0.4.
    private const double OverallPrecisionFloor = 0.15; // measured 0.165
    private const double OverallRecallFloor = 0.70; // measured 0.713 (0.7125)
    private const double OverallMrrFloor = 0.80; // measured 0.800
    private const double EnglishPrecisionFloor = 0.15; // measured 0.190
    private const double EnglishRecallFloor = 0.85; // measured 0.850
    private const double EnglishMrrFloor = 0.95; // measured 0.950
    private const double ArabicPrecisionFloor = 0.10; // measured 0.140
    // Arabic recall stays under the plan's 0.70 tuning trigger: the misses left are broken plurals, verbs against verbal
    // nouns, attached one-letter words and dialect vocabulary, which function words and suffixes cannot reach. On this
    // synthetic set the gap is indicative only: the spec §9 decision rule on semantic retrieval needs the real approved
    // directory and an approved query set (D-95).
    private const double ArabicRecallFloor = 0.55; // measured 0.575
    private const double ArabicMrrFloor = 0.65; // measured 0.650
    private const double FalsePositiveCeiling = 0.15; // measured 0.125 (one of eight; Arabic 0.250, English 0.000)

    private sealed record Run(EvaluationQuery Query, IReadOnlyList<string> Codes);

    private sealed record Slice(
        string Name, int Queries, double Precision, double Recall, double Mrr, int OutOfScope, double FalsePositiveRate);

    [Fact]
    public async Task Deterministic_retrieval_meets_its_floors_on_the_synthetic_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var set = EvaluationSet.Load();
        await set.EnsureDirectoryAsync(factory, cancellationToken);
        using var participant = factory.AsParticipant();

        // One untimed search first, so start-up work (compilation, the first connection) is not counted as latency.
        await SearchAsync(participant, set.Queries[0], cancellationToken);
        var runs = new List<Run>();
        var latencies = new List<double>();
        foreach (var query in set.Queries)
        {
            var started = Stopwatch.GetTimestamp();
            var found = await SearchAsync(participant, query, cancellationToken);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)null), (found.Mode, found.Fallback));
            runs.Add(new Run(query, found.Matches.Select(match => match.Code).ToList()));
        }

        var staffOnly = set.Resources.Where(resource => resource.Visibility == "staff_only").Select(resource => resource.Code);
        Assert.Empty(runs.SelectMany(run => run.Codes).Intersect(staffOnly));
        var overall = Measure("Overall", runs);
        var english = Measure("English", runs.Where(run => run.Query.Language == "en").ToList());
        var arabic = Measure("Arabic", runs.Where(run => run.Query.Language == "ar").ToList());
        var p50 = RetrievalMetrics.Percentile(latencies, 50);
        var p95 = RetrievalMetrics.Percentile(latencies, 95);
        TestContext.Current.TestOutputHelper?.WriteLine(Report(set, [overall, english, arabic], latencies.Count, p50, p95));

        AtLeast(overall.Precision, OverallPrecisionFloor, "overall P@5");
        AtLeast(overall.Recall, OverallRecallFloor, "overall R@5");
        AtLeast(overall.Mrr, OverallMrrFloor, "overall MRR");
        AtLeast(english.Precision, EnglishPrecisionFloor, "English P@5");
        AtLeast(english.Recall, EnglishRecallFloor, "English R@5");
        AtLeast(english.Mrr, EnglishMrrFloor, "English MRR");
        AtLeast(arabic.Precision, ArabicPrecisionFloor, "Arabic P@5");
        AtLeast(arabic.Recall, ArabicRecallFloor, "Arabic R@5");
        AtLeast(arabic.Mrr, ArabicMrrFloor, "Arabic MRR");
        Assert.True(
            overall.FalsePositiveRate <= FalsePositiveCeiling + Tolerance,
            $"The out-of-scope false-positive rate {Format(overall.FalsePositiveRate)} rose above its ceiling {Format(FalsePositiveCeiling)}.");
    }

    private static async Task<SupportResourceDiscoveryResponse> SearchAsync(
        HttpClient client, EvaluationQuery query, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync(
            SearchRoute, new { text = query.Text, categories = query.Categories, limit = K }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<SupportResourceDiscoveryResponse>(cancellationToken);
    }

    private static Slice Measure(string name, IReadOnlyList<Run> runs)
    {
        var inScope = runs.Where(run => run.Query.InScope).ToList();
        var outOfScope = runs.Where(run => !run.Query.InScope).ToList();
        HashSet<string> Relevant(Run run) => run.Query.Relevant.ToHashSet(StringComparer.Ordinal);
        return new Slice(
            name,
            inScope.Count,
            inScope.Average(run => RetrievalMetrics.PrecisionAtK(run.Codes, Relevant(run), K)),
            inScope.Average(run => RetrievalMetrics.RecallAtK(run.Codes, Relevant(run), K)),
            inScope.Average(run => RetrievalMetrics.ReciprocalRank(run.Codes, Relevant(run))),
            outOfScope.Count,
            (double)outOfScope.Count(run => run.Codes.Count > 0) / outOfScope.Count);
    }

    private static string Report(EvaluationSet set, IReadOnlyList<Slice> slices, int searches, double p50, double p95)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Deterministic retrieval, synthetic set ({set.Resources.Count} resources, participant view, limit {K}):");
        report.AppendLine("| Slice | In-scope queries | P@5 | R@5 | MRR | Out-of-scope queries | False-positive rate |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var slice in slices)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {slice.Name} | {slice.Queries} | {Format(slice.Precision)} | {Format(slice.Recall)} | {Format(slice.Mrr)} " +
                $"| {slice.OutOfScope} | {Format(slice.FalsePositiveRate)} |");
        }

        report.Append(CultureInfo.InvariantCulture,
            $"Latency over {searches} searches after one untimed warm-up: p50 {p50:0.0} ms, p95 {p95:0.0} ms.");
        return report.ToString();
    }

    private static void AtLeast(double measured, double floor, string metric) =>
        Assert.True(measured >= floor - Tolerance, $"{metric} {Format(measured)} fell below its floor {Format(floor)}.");

    private static string Format(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
