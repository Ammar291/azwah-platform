namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One live conversation with the voice model. The server sends the microphone's audio as it arrives and relays the
/// events as they come; it reads the events from one reader at a time. Disposing ends the model session.
/// </summary>
public interface IVoiceConsultantSession : IAsyncDisposable
{
    /// <summary>Microphone audio: PCM 16-bit little-endian mono at <see cref="ConsultantProtocol.InputSampleRate"/>.</summary>
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken);

    /// <summary>The model's events until the session ends. A fault ends the live session with <c>model_failed</c>.</summary>
    IAsyncEnumerable<VoiceConsultantEvent> ReadEventsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks the model to say <paramref name="words"/> now, as they are, in the session's own voice, then listen (D-141).
    /// The words are always Azwah's (the supportive reply or the fixed words), never the person's.
    /// </summary>
    Task SpeakAsync(string words, CancellationToken cancellationToken);
}
