using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>
/// Runs every in-scope evaluation query through the dormant language-model path with scripted models (spec §9) and
/// measures what reaches a participant. This measures the guard, not any real model: no provider is approved or
/// configured, and the scripted models run only in test hosts. The report prints aggregates only, never query text. To
/// see the table, run the command in <see cref="RetrievalEvaluationTests"/>.
/// </summary>
[Collection(SupportResourceEvaluationCollection.Name)]
public sealed class AssistantGuardEvaluationTests(ApiFactory factory)
{
    private const string SearchRoute = "/api/v1/support-resources/search";
    private const int Limit = 5;

    // Example prices only, to show how a cost would be worked out from the token counts; no provider is chosen.
    private const double InputUsdPerMillionTokens = 3;
    private const double OutputUsdPerMillionTokens = 15;

    private static readonly string[] AlwaysFallBack =
        [EvaluationModels.Fabricating, EvaluationModels.SchemaBreaking, EvaluationModels.Slow];

    private static readonly string[] NeverFallBack = [EvaluationModels.Honest, EvaluationModels.FlakyThenHonest];

    private sealed record Search(
        SupportResourceDiscoveryResponse Response,
        double Milliseconds,
        IReadOnlyList<(LanguageModelRequest Request, string Json)> Replies)
    {
        /// <summary>A deterministic answer without a fallback code never called the model (no terms or no candidates).</summary>
        public bool ReachedModel => Response.Mode == DiscoveryMode.LanguageModel || Response.Fallback is not null;
    }

    private sealed record Measured(
        string Behaviour,
        int Searches,
        int Reached,
        int ModelCalls,
        int DistinctModelRequests,
        int RawItems,
        int RawGrounded,
        int SchemaFailures,
        int Matches,
        int InvalidMatches,
        int Fallbacks,
        IReadOnlyList<double> ReachedLatencies,
        double InputTokens,
        double OutputTokens,
        int InjectedSearches,
        int InjectedAccepted)
    {
        public double FallbackRate => Rate(Fallbacks, Reached);

        public double Cost =>
            (InputTokens * InputUsdPerMillionTokens + OutputTokens * OutputUsdPerMillionTokens) / 1_000_000;

        /// <summary>The example cost scaled to 1,000 searches that reach the model.</summary>
        public double CostPerThousand => Reached == 0 ? 0 : Cost / Reached * 1000;
    }

    [Fact]
    public async Task The_guard_keeps_every_final_recommendation_grounded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var set = EvaluationSet.Load();
        var directory = await set.EnsureDirectoryAsync(factory, cancellationToken);
        var inScope = set.InScope;

        var measured = new List<Measured>();
        foreach (var behaviour in EvaluationModels.All)
        {
            measured.Add(await RunAsync(behaviour, inScope, directory, cancellationToken));
        }

        TestContext.Current.TestOutputHelper?.WriteLine(Report(measured));

        Assert.All(measured, result =>
        {
            Assert.True(result.Reached > 0, $"{result.Behaviour}: no search reached the model.");
            Assert.True(result.Matches > 0, $"{result.Behaviour}: no search returned a match.");
            Assert.Equal(0, result.InvalidMatches);
            // The denominator rule: exactly the searches with a model answer or a fallback code were sent to the model.
            Assert.Equal(result.Reached, result.DistinctModelRequests);
        });
        Assert.All(measured.Where(result => AlwaysFallBack.Contains(result.Behaviour)), result =>
            Assert.Equal(1.0, result.FallbackRate));
        // Controls: a model that keeps to the contract is used, so the fallbacks above come from the guard and the timeout.
        Assert.All(measured.Where(result => NeverFallBack.Contains(result.Behaviour)), result =>
            Assert.Equal(0.0, result.FallbackRate));
        Assert.Equal(
            [
                EvaluationModels.Honest, EvaluationModels.Fabricating, EvaluationModels.InjectionFollowing,
                EvaluationModels.SchemaBreaking, EvaluationModels.Slow, EvaluationModels.FlakyThenHonest,
            ],
            measured.Select(result => result.Behaviour));
    }

    private async Task<Measured> RunAsync(
        EvaluationBehaviour behaviour,
        IReadOnlyList<EvaluationQuery> queries,
        IReadOnlyDictionary<string, SupportResourceResponse> directory,
        CancellationToken cancellationToken)
    {
        var model = behaviour.Create();
        await using var host = ModelHost(model, behaviour.Settings);
        using var measurements = new SupportResourceMeasurements(host);
        using var participant = host.CreateClient();
        participant.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ApiClients.NewSubject("participant"));
        participant.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, Roles.Participant);

        var searches = new List<Search>();
        foreach (var query in queries.Take(behaviour.QueryCount ?? queries.Count))
        {
            var repliesBefore = model.Replies.Count;
            var started = Stopwatch.GetTimestamp();
            using var response = await participant.PostJsonAsync(
                SearchRoute, new { text = query.Text, categories = query.Categories, limit = Limit }, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var found = await response.ReadAsync<SupportResourceDiscoveryResponse>(cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            searches.Add(new Search(found, elapsed, model.Replies.Skip(repliesBefore).ToList()));
        }

        var reached = searches.Where(search => search.ReachedModel).ToList();
        var raw = reached
            .SelectMany(search => search.Replies)
            .Select(reply => RawItems(reply.Request, reply.Json))
            .ToList();
        var injected = reached
            .Where(search => search.Replies.Any(reply => EvaluationModels.Injected(EvaluationModels.Read(reply.Request)) is not null))
            .ToList();
        var matches = searches.SelectMany(search => search.Response.Matches).ToList();
        return new Measured(
            behaviour.Name,
            searches.Count,
            reached.Count,
            model.Requests.Count,
            model.Requests.Distinct(ReferenceEqualityComparer.Instance).Count(),
            raw.Sum(items => items.Items),
            raw.Sum(items => items.Grounded),
            reached.Count(search => search.Response.Fallback == DiscoveryFallback.ModelOutputInvalid),
            matches.Count,
            matches.Count(match => !IsApprovedAndVisible(match, directory)),
            reached.Count(search => search.Response.Fallback is not null),
            reached.Select(search => search.Milliseconds).ToList(),
            measurements.Total("azwah.support_resources.model_tokens", ("direction", "input")),
            measurements.Total("azwah.support_resources.model_tokens", ("direction", "output")),
            behaviour.Name == EvaluationModels.InjectionFollowing ? injected.Count : 0,
            behaviour.Name == EvaluationModels.InjectionFollowing
                ? injected.Count(search => search.Response.Mode == DiscoveryMode.LanguageModel)
                : 0);
    }

    /// <summary>A host with the model switched on (approval reference <c>TEST-ONLY</c>) and the scripted model registered.</summary>
    private WebApplicationFactory<Program> ModelHost(
        ILanguageModelClient model, IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = new Dictionary<string, string?>(settings)
        {
            ["SupportResources:LanguageModel:Enabled"] = "true",
            ["SupportResources:LanguageModel:ApprovalReference"] = "TEST-ONLY",
        };
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, sources) => sources.AddInMemoryCollection(configuration));
            builder.ConfigureTestServices(services => services.AddSingleton(model));
        });
    }

    /// <summary>
    /// The items of a raw reply's <c>selected</c> array, and how many of them are handles this request offered. A reply
    /// without a readable array has no items: it counts only as a schema failure.
    /// </summary>
    private static (int Items, int Grounded) RawItems(LanguageModelRequest request, string json)
    {
        var offered = EvaluationModels.Read(request).Candidates
            .Select(candidate => candidate.Handle)
            .ToHashSet(StringComparer.Ordinal);
        try
        {
            using var reply = JsonDocument.Parse(json);
            if (reply.RootElement.ValueKind != JsonValueKind.Object
                || !reply.RootElement.TryGetProperty("selected", out var selected)
                || selected.ValueKind != JsonValueKind.Array)
            {
                return (0, 0);
            }

            var items = selected.EnumerateArray().ToList();
            var grounded = items.Count(item =>
                item.ValueKind == JsonValueKind.String && offered.Contains(item.GetString() ?? string.Empty));
            return (items.Count, grounded);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }

    /// <summary>The match is an approved directory row a participant may see, rendered exactly from its approved fields.</summary>
    private static bool IsApprovedAndVisible(
        SupportResourceMatchResponse match, IReadOnlyDictionary<string, SupportResourceResponse> directory) =>
        directory.TryGetValue(match.Code, out var row)
        && row.Status == SupportResourceStatus.Approved
        && row.Visibility == SupportResourceVisibility.Everyone
        && (row.Id, row.VersionNumber, row.Url) == (match.Id, match.VersionNumber, match.Url)
        && (row.NameEn, row.NameAr, row.SummaryEn, row.SummaryAr, row.ProviderNameEn, row.ProviderNameAr)
            == (match.NameEn, match.NameAr, match.SummaryEn, match.SummaryAr, match.ProviderNameEn, match.ProviderNameAr);

    private static string Report(IReadOnlyList<Measured> measured)
    {
        string[] columns =
        [
            "Behaviour", "Searches", "Reached model", "Model calls", "Raw items", "Raw invalid rate", "Raw grounding rate",
            "Schema-failure rate", "Final invalid rate", "Fallback rate", "Latency p50 (ms)", "Latency p95 (ms)",
            "Input tokens", "Output tokens", "Example cost (USD)", "Example cost per 1,000 searches (USD)",
        ];
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Language-model guard, scripted models, synthetic set (participant view, limit {Limit}):");
        report.AppendLine(Row(columns));
        report.AppendLine(Row(columns.Select(_ => "---")));
        foreach (var result in measured)
        {
            report.AppendLine(Row(
            [
                result.Behaviour,
                Count(result.Searches),
                Count(result.Reached),
                Count(result.ModelCalls),
                Count(result.RawItems),
                RateOrNone(result.RawItems - result.RawGrounded, result.RawItems),
                RateOrNone(result.RawGrounded, result.RawItems),
                Format(Rate(result.SchemaFailures, result.Reached)),
                Format(Rate(result.InvalidMatches, result.Matches)),
                Format(result.FallbackRate),
                Latency(result.ReachedLatencies, 50),
                Latency(result.ReachedLatencies, 95),
                result.InputTokens.ToString("0", CultureInfo.InvariantCulture),
                result.OutputTokens.ToString("0", CultureInfo.InvariantCulture),
                result.Cost.ToString("0.0000", CultureInfo.InvariantCulture),
                result.CostPerThousand.ToString("0.00", CultureInfo.InvariantCulture),
            ]));
        }

        var injection = measured.Single(result => result.Behaviour == EvaluationModels.InjectionFollowing);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"Injection-following: an entry carrying injected text was among the candidates in {injection.InjectedSearches} " +
            $"of {injection.Reached} searches that reached the model; {injection.InjectedAccepted} of those answers passed " +
            $"the guard (narrowed to the injecting entry, an approved row: the residual risk of spec §7.5) and " +
            $"{injection.InjectedSearches - injection.InjectedAccepted} were rejected.");
        report.AppendLine(
            "Model rates (raw, schema failure, fallback, latency) count only searches that reached the model; a " +
            "deterministic answer with no fallback code never called it. Raw rates count the items of each reply's " +
            "\"selected\" array before the guard; a reply without a readable array has none. The final invalid rate counts " +
            "every match returned.");
        report.Append(CultureInfo.InvariantCulture,
            $"Tokens are estimated by the scripted models at four characters per token. Costs use example prices, no " +
            $"provider chosen: USD {InputUsdPerMillionTokens:0} per million input tokens and USD " +
            $"{OutputUsdPerMillionTokens:0} per million output tokens.");
        return report.ToString();
    }

    private static string Row(IEnumerable<string> cells) => "| " + string.Join(" | ", cells) + " |";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Rate(int count, int total) => total == 0 ? 0 : (double)count / total;

    private static string RateOrNone(int count, int total) => total == 0 ? "n/a" : Format(Rate(count, total));

    private static string Latency(IReadOnlyList<double> values, double percentile) =>
        values.Count == 0
            ? "n/a"
            : RetrievalMetrics.Percentile(values, percentile).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Format(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
}
