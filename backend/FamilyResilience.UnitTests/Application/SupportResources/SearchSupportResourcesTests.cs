using System.Diagnostics.Metrics;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.UnitTests.Application.Safeguarding;
using FamilyResilience.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Application.SupportResources;

/// <summary>
/// The directory search as a participant types it (D-135, D-138): the crisis screen, the recording beside the search,
/// and the notice on a success or a fault.
/// </summary>
public sealed class SearchSupportResourcesTests
{
    private const string CrisisText = "I want to end my life";

    private readonly CapturedLogs<SearchSupportResources> _logs = new();

    /// <summary>A directory that answers nothing, or runs <see cref="Discover"/>; <see cref="Asked"/> says it was called.</summary>
    private sealed class ScriptedDirectory : ISupportResourceAssistant
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Asked => _asked.Task;

        public Func<SupportResourceDiscovery> Discover { get; init; } = () => new SupportResourceDiscovery(DiscoveryMode.Deterministic, null, []);

        public Task<SupportResourceDiscovery> DiscoverAsync(SupportResourceQuery query, CancellationToken cancellationToken)
        {
            _asked.TrySetResult();
            return Task.FromResult(Discover());
        }
    }

    private SearchSupportResources Search(ISupportResourceAssistant directory, ISafetySignalRecorder recorder, string role = Roles.Participant) =>
        new(
            directory,
            new FakeCurrentUser(true, "subject", role),
            new CrisisSignalScreen(CrisisSignalCatalog.Load()),
            recorder,
            new SupportResourceMetrics(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>()),
            new FixedTimeProvider(Now),
            _logs);

    private static SearchSupportResourcesRequest Request(string text) => new(text, null, null, null);

    [Fact]
    public async Task A_participants_crisis_search_is_recorded_and_answered_with_the_notice()
    {
        var recorder = new RecordingSafetySignalRecorder();

        var outcome = await Search(new ScriptedDirectory(), recorder).HandleAsync(Request(CrisisText), TestContext.Current.CancellationToken);

        Assert.True(outcome.Result.IsSuccess);
        Assert.Equal((SafetyNotices.CrisisSupport, SafetyNotices.CrisisSupport), (outcome.SafetyNotice, outcome.Result.Value.SafetyNotice));
        Assert.Equal(
            new SafetySignal(SafetyAlertSource.SupportSearch, SafetyConcern.SuicideOrSelfHarm, SafetyDetector.PhraseScreen, "suicide.intent", null),
            Assert.Single(recorder.Signals));
    }

    [Fact]
    public async Task A_discovery_that_throws_after_a_crisis_match_still_carries_the_notice()
    {
        // The notice must never depend on the database (D-138).
        var recorder = new RecordingSafetySignalRecorder();
        var directory = new ScriptedDirectory { Discover = () => throw new InvalidOperationException("Synthetic database fault quoting a row.") };

        var outcome = await Search(directory, recorder).HandleAsync(Request(CrisisText), TestContext.Current.CancellationToken);

        Assert.Equal((SupportResourceSearchErrors.Faulted, SafetyNotices.CrisisSupport), (outcome.Result.Error, outcome.SafetyNotice));
        Assert.Single(recorder.Calls);
        Assert.Equal(["A directory search stopped on a fault of type InvalidOperationException after a crisis signal."], _logs.Lines);
    }

    [Fact]
    public async Task A_discovery_that_throws_without_a_crisis_match_propagates()
    {
        var directory = new ScriptedDirectory { Discover = () => throw new InvalidOperationException("Synthetic database fault.") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Search(directory, new RecordingSafetySignalRecorder()).HandleAsync(Request("help with debts"), TestContext.Current.CancellationToken));

        Assert.Empty(_logs.Lines);
    }

    [Fact]
    public async Task The_search_starts_while_the_recording_is_still_running_and_the_answer_waits_for_it()
    {
        // A slow database must not hold the search back by up to the recording budget.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new RecordingSafetySignalRecorder { Hold = hold.Task };
        var directory = new ScriptedDirectory();

        var searching = Search(directory, recorder).HandleAsync(Request(CrisisText), TestContext.Current.CancellationToken);
        await directory.Asked.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var answeredBeforeTheRecording = searching.IsCompleted;
        hold.SetResult();
        var outcome = await searching;

        Assert.False(answeredBeforeTheRecording, "The answer must wait for the recording before it returns.");
        Assert.Equal(SafetyNotices.CrisisSupport, outcome.SafetyNotice);
        Assert.False(Assert.Single(recorder.Calls).Token.CanBeCanceled);
    }

    [Fact]
    public async Task A_staff_search_is_never_screened()
    {
        var recorder = new RecordingSafetySignalRecorder();

        var outcome = await Search(new ScriptedDirectory(), recorder, Roles.SupportCoordinator).HandleAsync(Request(CrisisText), TestContext.Current.CancellationToken);

        Assert.Null(outcome.SafetyNotice);
        Assert.Null(outcome.Result.Value.SafetyNotice);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public void An_outcome_never_prints_more_than_codes()
    {
        var outcome = new SupportSearchOutcome(Result.Failure<SupportResourceDiscoveryResponse>(SupportResourceSearchErrors.Faulted), SafetyNotices.CrisisSupport);

        Assert.Equal(
            "SupportSearchOutcome { IsSuccess = False, Error = support_resources.search_faulted, SafetyNotice = crisis_support }",
            outcome.ToString());
    }
}
