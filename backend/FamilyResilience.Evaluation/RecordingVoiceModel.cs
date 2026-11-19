using System.Diagnostics;
using FamilyResilience.Application.Assistant;

namespace FamilyResilience.Evaluation;

/// <summary>
/// The real voice model with a stopwatch on it (D-141): when each session was asked for, opened (and how), and when it
/// was asked to say Azwah's words, which is when the call hears the person again. Records times and codes only.
/// </summary>
internal sealed class RecordingVoiceModel(IVoiceConsultantModel inner, Stopwatch clock) : IVoiceConsultantModel
{
    private readonly List<OpenedSession> _opened = [];

    public bool IsConfigured => inner.IsConfigured;

    public IReadOnlyList<OpenedSession> Opened
    {
        get
        {
            lock (_opened)
            {
                return [.. _opened];
            }
        }
    }

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken) =>
        OpenAsync(Task.FromResult(request), cancellationToken);

    /// <summary>Passes the request on unfinished, so the real adapter connects while it is prepared, as in the API.</summary>
    public async Task<IVoiceConsultantSession> OpenAsync(Task<VoiceConsultantRequest> preparing, CancellationToken cancellationToken)
    {
        var asked = clock.Elapsed;
        var session = await inner.OpenAsync(preparing, cancellationToken);
        var request = await preparing;
        var opened = new OpenedSession(asked, clock.Elapsed, request.Opening, request.Note is not null, request.History.Count, request.Memory is not null);
        lock (_opened)
        {
            _opened.Add(opened);
        }

        return new Session(session, opened, clock);
    }

    /// <summary>One session of the call: when it was asked for and opened, how it was started, and when it was asked to speak.</summary>
    internal sealed record OpenedSession(TimeSpan Asked, TimeSpan Opened, ConsultantOpening Opening, bool HasNote, int History, bool HasMemory)
    {
        public TimeSpan? Spoke { get; set; }
    }

    private sealed class Session(IVoiceConsultantSession inner, OpenedSession opened, Stopwatch clock) : IVoiceConsultantSession
    {
        public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken) => inner.SendAudioAsync(pcm, cancellationToken);

        public IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync(CancellationToken cancellationToken) => inner.ReadEventsAsync(cancellationToken);

        public Task SpeakAsync(string words, CancellationToken cancellationToken)
        {
            opened.Spoke = clock.Elapsed;
            return inner.SpeakAsync(words, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
