using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.SupportResources;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Application.SupportResources.ScriptedLanguageModelClient;

namespace FamilyResilience.UnitTests.Application.SupportResources;

/// <summary>
/// The optional model path: it may only choose and order candidates the deterministic search found, and every failure
/// answers with the deterministic search instead. The client is scripted; no provider exists.
/// </summary>
public sealed class LanguageModelSupportResourceAssistantTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

    private readonly SupportResource _first = SyntheticSupportResources.Make("synthetic_first");
    private readonly SupportResource _second = SyntheticSupportResources.Make("synthetic_second");
    private readonly SupportResource _third = SyntheticSupportResources.Make("synthetic_third");

    public void Dispose() => _services.Dispose();

    private static LanguageModelSettings Settings(int maxAttempts = 2, int maxCandidates = 12) =>
        new(true, "TEST-ONLY", 1, maxAttempts, maxCandidates, 256);

    private static SupportResourceQuery Query(string? text = "debt advice", string[]? categories = null, int limit = 5) =>
        new(text, SupportResourceQueryText.Parse(text), categories ?? [], [], [SupportResourceVisibility.Everyone], limit);

    private FakeSupportResourceSearch Candidates() => new(_first, _second, _third) { StrictAnswer = [_second] };

    private LanguageModelSupportResourceAssistant Assistant(
        FakeSupportResourceSearch search, ILanguageModelClient client, LanguageModelSettings? settings = null)
    {
        var time = new FixedTimeProvider(Now);
        return new LanguageModelSupportResourceAssistant(
            new DeterministicSupportResourceAssistant(search, time),
            search,
            client,
            settings ?? Settings(),
            new SupportResourceMetrics(_services.GetRequiredService<IMeterFactory>()),
            time);
    }

    private static void AssertStrictFallback(
        SupportResourceDiscovery answer, DiscoveryFallback fallback, FakeSupportResourceSearch search, SupportResource strict)
    {
        Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)fallback), (answer.Mode, answer.Fallback));
        Assert.Same(strict, Assert.Single(answer.Matches));
        Assert.Equal(TermCoverage.Half, search.Searches[^1].Coverage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("!!! 123")]
    public async Task Without_text_the_model_is_not_asked(string? text)
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r1"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(text, ["synthetic_support"]), TestContext.Current.CancellationToken);

        Assert.Empty(client.Requests);
        Assert.Equal((DiscoveryMode.Deterministic, (DiscoveryFallback?)null), (answer.Mode, answer.Fallback));
        Assert.Equal(TermCoverage.Half, Assert.Single(search.Searches).Coverage);
    }

    [Fact]
    public async Task Without_candidates_the_model_is_not_asked_and_the_answer_is_no_match()
    {
        var search = new FakeSupportResourceSearch();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r1"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Empty(client.Requests);
        Assert.Equal(
            (DiscoveryMode.Deterministic, (DiscoveryFallback?)null, DiscoveryOutcome.NoMatch),
            (answer.Mode, answer.Fallback, answer.Outcome));
        Assert.Empty(answer.Matches);
        Assert.Single(search.Searches);
    }

    [Fact]
    public async Task An_accepted_selection_is_rendered_from_directory_rows_in_the_models_order()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r3","r1"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal((DiscoveryMode.LanguageModel, (DiscoveryFallback?)null), (answer.Mode, answer.Fallback));
        Assert.Equal(2, answer.Matches.Count);
        Assert.Same(_third, answer.Matches[0]);
        Assert.Same(_first, answer.Matches[1]);
        Assert.Single(search.Searches);
    }

    [Fact]
    public async Task An_empty_selection_falls_back_to_the_strict_answer()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":[]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(limit: 3), TestContext.Current.CancellationToken);

        AssertStrictFallback(answer, DiscoveryFallback.ModelSelectedNone, search, _second);
        Assert.Equal(3, search.Searches[^1].Limit);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"selected":["r1"],"reason":"Synthetic"}""")]
    [InlineData("""{"selected":["r1","r1"]}""")]
    public async Task Invalid_output_falls_back_without_a_retry(string reply)
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply(reply), Reply("""{"selected":["r1"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Single(client.Requests);
        AssertStrictFallback(answer, DiscoveryFallback.ModelOutputInvalid, search, _second);
    }

    [Fact]
    public async Task Ungrounded_output_falls_back_without_a_retry()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r1","r9"]}"""), Reply("""{"selected":["r1"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Single(client.Requests);
        AssertStrictFallback(answer, DiscoveryFallback.ModelOutputUngrounded, search, _second);
    }

    [Fact]
    public async Task A_timeout_is_retried_then_falls_back()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Hang());
        var settings = Settings(maxAttempts: 2);
        var clock = Stopwatch.StartNew();

        var answer = await Assistant(search, client, settings).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        clock.Stop();
        Assert.Equal(settings.MaxAttempts, client.Requests.Count);
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds((settings.TimeoutSeconds * settings.MaxAttempts) + 1),
            $"Took {clock.Elapsed}.");
        AssertStrictFallback(answer, DiscoveryFallback.ModelTimeout, search, _second);
    }

    [Fact]
    public async Task A_model_that_ignores_cancellation_is_still_timed_out()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(HangIgnoringCancellation());
        var settings = Settings(maxAttempts: 2);
        var clock = Stopwatch.StartNew();

        var answer = await Assistant(search, client, settings).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        clock.Stop();
        Assert.Equal(settings.MaxAttempts, client.Requests.Count);
        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds((settings.TimeoutSeconds * settings.MaxAttempts) + 1),
            $"Took {clock.Elapsed}.");
        AssertStrictFallback(answer, DiscoveryFallback.ModelTimeout, search, _second);
    }

    [Fact]
    public async Task A_transient_fault_is_retried_and_can_succeed()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(
            Throw(new LanguageModelUnavailableException(transient: true)), Reply("""{"selected":["r2"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal((DiscoveryMode.LanguageModel, (DiscoveryFallback?)null), (answer.Mode, answer.Fallback));
        Assert.Same(_second, Assert.Single(answer.Matches));
    }

    [Fact]
    public async Task Transient_faults_stop_after_the_last_attempt()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Throw(new LanguageModelUnavailableException(transient: true)));

        var answer = await Assistant(search, client, Settings(maxAttempts: 3)).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(3, client.Requests.Count);
        AssertStrictFallback(answer, DiscoveryFallback.ModelUnavailable, search, _second);
    }

    [Fact]
    public async Task A_permanent_fault_is_not_retried()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(
            Throw(new LanguageModelUnavailableException(transient: false)), Reply("""{"selected":["r2"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Single(client.Requests);
        AssertStrictFallback(answer, DiscoveryFallback.ModelUnavailable, search, _second);
    }

    [Theory]
    [InlineData("invalid_operation")]
    [InlineData("http")]
    [InlineData("cancelled_by_the_client_itself")]
    [InlineData("null_reply")]
    public async Task An_unexpected_exception_falls_back_as_unavailable(string kind)
    {
        var search = Candidates();
        Func<CancellationToken, Task<LanguageModelReply>> step = kind switch
        {
            "invalid_operation" => Throw(new InvalidOperationException("Synthetic adapter bug")),
            "http" => Throw(new HttpRequestException("Synthetic network fault")),
            // An adapter's own timeout: cancelled, but neither by the caller nor by the assistant's timer.
            "cancelled_by_the_client_itself" => Throw(new TaskCanceledException()),
            // A broken adapter can answer null although the port says it never does; the ! says so on purpose, to prove
            // the assistant survives it.
            _ => _ => Task.FromResult<LanguageModelReply>(null!),
        };
        var client = new ScriptedLanguageModelClient(step, Reply("""{"selected":["r2"]}"""));

        var answer = await Assistant(search, client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Single(client.Requests);
        AssertStrictFallback(
            answer, kind == "null_reply" ? DiscoveryFallback.ModelOutputInvalid : DiscoveryFallback.ModelUnavailable, search, _second);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_fallback()
    {
        var search = Candidates();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var hang = Hang();
        // The caller leaves while the model is working: no fixed delay, so a busy machine cannot cancel too early.
        var client = new ScriptedLanguageModelClient(async cancellationToken =>
        {
            var waiting = hang(cancellationToken);
            await caller.CancelAsync();
            return await waiting;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Assistant(search, client).DiscoverAsync(Query(), caller.Token));

        Assert.Single(client.Requests);
        Assert.Single(search.Searches);
    }

    [Fact]
    public async Task Caller_cancellation_wins_over_a_client_fault()
    {
        var search = Candidates();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var client = new ScriptedLanguageModelClient(_ =>
        {
            caller.Cancel();
            throw new HttpRequestException("Synthetic fault while the caller left");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Assistant(search, client).DiscoverAsync(Query(), caller.Token));

        Assert.Single(search.Searches);
    }

    [Fact]
    public async Task The_model_receives_only_redacted_text_and_handles()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r1"]}"""));

        await Assistant(search, client).DiscoverAsync(
            Query("debt advice 0501234567 synthetic.person@example.org https://example.org/me"), TestContext.Current.CancellationToken);

        var request = Assert.Single(client.Requests);
        Assert.Equal(SupportResourceSelectionPrompt.Instructions, request.Instructions);
        Assert.Equal(SupportResourceSelectionPrompt.JsonSchema, request.JsonSchema);
        Assert.Equal(256, request.MaxOutputTokens);
        using var data = JsonDocument.Parse(request.Data);
        Assert.Equal("debt advice", data.RootElement.GetProperty("query").GetString());
        Assert.Equal(
            ["r1", "r2", "r3"],
            data.RootElement.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("handle").GetString()));
        Assert.DoesNotContain("0501234567", request.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic.person", request.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("example.org", request.Data, StringComparison.Ordinal);
        foreach (var resource in new[] { _first, _second, _third })
        {
            Assert.DoesNotContain(resource.Id.Value.ToString(), request.Data, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(resource.Code, request.Data, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Candidates_use_any_term_coverage_and_the_candidate_cap()
    {
        var search = Candidates();
        var client = new ScriptedLanguageModelClient(Reply("""{"selected":["r1"]}"""));
        var query = Query("debt advice", ["synthetic_support"], limit: 2);

        await Assistant(search, client, Settings(maxCandidates: 7)).DiscoverAsync(query, TestContext.Current.CancellationToken);

        var criteria = Assert.Single(search.Searches);
        Assert.Equal((TermCoverage.AnyTerm, 7, Now), (criteria.Coverage, criteria.Limit, criteria.Now));
        Assert.Equal(query.Terms, criteria.Terms);
        Assert.Equal(query.Categories, criteria.Categories);
        Assert.Same(query.Visibilities, criteria.Visibilities);
    }

    [Fact]
    public async Task Model_calls_and_tokens_are_counted()
    {
        var factory = _services.GetRequiredService<IMeterFactory>();
        var counts = new List<(string Instrument, long Value, string Tag)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SupportResourceMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            counts.Add((instrument.Name, value, tags[0].Value?.ToString() ?? string.Empty)));
        listener.Start();
        var client = new ScriptedLanguageModelClient(
            Throw(new LanguageModelUnavailableException(transient: true)),
            Reply("""{"selected":["r1","r9"]}""", inputTokens: 40, outputTokens: 3),
            Reply("""{"selected":["r2"]}""", inputTokens: 100, outputTokens: 7));

        await Assistant(Candidates(), client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);
        await Assistant(Candidates(), client).DiscoverAsync(Query(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["unavailable", "ungrounded", "ok"],
            counts.Where(c => c.Instrument == "azwah.support_resources.model_calls").Select(c => c.Tag));
        Assert.Equal(
            [("input", 40L), ("output", 3L), ("input", 100L), ("output", 7L)],
            counts.Where(c => c.Instrument == "azwah.support_resources.model_tokens").Select(c => (c.Tag, c.Value)));
    }
}
