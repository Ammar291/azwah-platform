using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// The crisis signals of one live call (D-135, D-138). Each final line from the person goes through the phrase screen,
/// and so does the line they were still saying when the call ended (<see cref="ObserveCallEnd"/>); a
/// <see cref="ConsultantSafetyFlag"/> from the model session (its safety check or the provider's filter) is a signal too.
/// A concern is recorded for staff at most once a call (source <c>consultant_voice</c>, the call's language) on the thread
/// pool, so the relay of audio never waits for the database, and with <see cref="CancellationToken.None"/>, so a person
/// who hangs up still raises the alert; the recorder bounds itself, and the relay waits for what is still recording
/// before the request ends (<see cref="SettleAsync"/>). The app gets the safety message once a call, and a phrase match
/// also offers SOS unless the call already offered it; both go out whatever becomes of the recording. Read by one reader
/// at a time (the relay's forwarding loop, then the end of the call). The text is screened and dropped: only the person's
/// growing line is held, in memory, until it settles or the call ends; nothing is kept, logged or recorded.
/// </summary>
internal sealed class ConsultantSafetyWatch(
    CrisisSignalScreen screen, ISafetySignalRecorder recorder, string language, TimeProvider timeProvider)
{
    /// <summary>The longest the relay waits at the end of a call for recordings: the recorder's own budget and a margin.</summary>
    public static readonly TimeSpan SettleLimit = RecordSafetySignal.RecordingLimit + TimeSpan.FromSeconds(1);

    private static readonly ReadOnlyMemory<byte> Sos = ConsultantMessages.Action(new ConsultantAction(AssistantScreens.Sos));

    private readonly HashSet<SafetyConcern> _recorded = [];
    private readonly List<Task> _recordings = [];
    private string? _growing;
    private bool _noticeSent;
    private bool _sosOffered;

    /// <summary>
    /// What to send the app after <paramref name="item"/>, in order: nothing, or the safety message (the first time in
    /// the call) and, for a phrase match, the SOS action (unless the call already offered it). A signal of a concern new
    /// to the call starts recording at once, without waiting for it, whether or not the app is still there.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Observe(VoiceConsultantEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item)
        {
            case ConsultantTranscript { Speaker: ConsultantSpeaker.User, Final: false } growing:
                // Held only in case the call ends before the line settles.
                _growing = growing.Text;
                return [];
            case ConsultantTranscript { Speaker: ConsultantSpeaker.User, Final: true } line:
                _growing = null;
                return screen.Screen(line.Text) is { } match
                    ? Raise(Signal(match.Concern, SafetyDetector.PhraseScreen, match.RuleCode), offerSos: true)
                    : [];
            case ConsultantSafetyFlag flag:
                // The session offers its own SOS beside a flag.
                return Raise(Signal(flag.Concern, flag.Detector, null), offerSos: false);
            case ConsultantAction { Action: AssistantScreens.Sos }:
                _sosOffered = true;
                return [];
            default:
                return [];
        }
    }

    /// <summary>
    /// Once nothing more is read from the call: the person's line that was still growing (the session settles it only
    /// when more words come or the session closes) is screened as said, and a match is recorded. Nothing is sent, because
    /// the app has gone.
    /// </summary>
    public void ObserveCallEnd()
    {
        if (_growing is { } text && screen.Screen(text) is { } match)
        {
            RecordOnce(Signal(match.Concern, SafetyDetector.PhraseScreen, match.RuleCode));
        }

        _growing = null;
    }

    /// <summary>
    /// Waits for the recordings still running, at most <see cref="SettleLimit"/> on the host's clock (the one the
    /// recorder's own budget runs on). A recording past the limit carries on alone, bounded by that budget.
    /// </summary>
    public async Task SettleAsync()
    {
        Task[] recordings;
        lock (_recordings)
        {
            recordings = [.. _recordings];
        }

        if (recordings.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(recordings).WaitAsync(SettleLimit, timeProvider);
        }
        catch (TimeoutException)
        {
            // The recorder logs and counts its own time-out; the call is over either way.
        }
    }

    private List<ReadOnlyMemory<byte>> Raise(SafetySignal signal, bool offerSos)
    {
        RecordOnce(signal);
        var messages = new List<ReadOnlyMemory<byte>>(2);
        if (!_noticeSent)
        {
            _noticeSent = true;
            messages.Add(ConsultantMessages.Safety);
        }

        if (offerSos && !_sosOffered)
        {
            _sosOffered = true;
            messages.Add(Sos);
        }

        return messages;
    }

    private SafetySignal Signal(SafetyConcern concern, SafetyDetector detector, string? code) =>
        new(SafetyAlertSource.ConsultantVoice, concern, detector, code, language);

    private void RecordOnce(SafetySignal signal)
    {
        if (!_recorded.Add(signal.Concern))
        {
            return;
        }

        // Never the call's token: hanging up must not drop the alert.
        var recording = Task.Run(() => recorder.RecordForCallerAsync(signal, CancellationToken.None));
        lock (_recordings)
        {
            _recordings.Add(recording);
        }
    }
}
