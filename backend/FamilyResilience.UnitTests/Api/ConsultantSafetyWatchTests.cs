using FamilyResilience.Api.Assistant;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.UnitTests.Application.Safeguarding;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Api;

/// <summary>The end of a call waits for its recordings on the host's clock, bounded by the settle limit.</summary>
public sealed class ConsultantSafetyWatchTests
{
    private static readonly CrisisSignalScreen Screen = new(CrisisSignalCatalog.Load());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task Settling_gives_up_on_a_held_recording_when_the_injected_clock_passes_the_limit()
    {
        var clock = new ManualTimeProvider(Now);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new RecordingSafetySignalRecorder { Hold = hold.Task };
        var watch = new ConsultantSafetyWatch(Screen, recorder, "en", clock);
        watch.Observe(new ConsultantTranscript(ConsultantSpeaker.User, "I want to end my life", Final: true));
        await WaitUntilAsync(() => recorder.Calls.Count == 1);

        var settling = watch.SettleAsync();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        var settledEarly = settling.IsCompleted;
        clock.Advance(ConsultantSafetyWatch.SettleLimit);
        // Well inside the real-time limit, so only the injected clock can have ended the wait.
        await settling.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        hold.SetResult();

        Assert.False(settledEarly, "Settling must wait for the recording until the limit passes on the injected clock.");
        Assert.Equal(SafetyDetector.PhraseScreen, Assert.Single(recorder.Signals).Detector);
    }

    [Fact]
    public async Task Settling_ends_as_soon_as_the_recordings_finish()
    {
        var recorder = new RecordingSafetySignalRecorder();
        var watch = new ConsultantSafetyWatch(Screen, recorder, "en", new ManualTimeProvider(Now));
        watch.Observe(new ConsultantTranscript(ConsultantSpeaker.User, "I want to end my life", Final: true));

        await watch.SettleAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Single(recorder.Calls);
    }
}
