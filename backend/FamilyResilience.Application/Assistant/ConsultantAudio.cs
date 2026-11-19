namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A piece of the consultant's spoken reply: PCM 16-bit little-endian mono at
/// <see cref="ConsultantProtocol.OutputSampleRate"/>. Never logged: <see cref="ToString"/> prints the length only.
/// </summary>
public sealed record ConsultantAudio(ReadOnlyMemory<byte> Pcm) : VoiceConsultantEvent
{
    public override string ToString() => $"ConsultantAudio {{ Bytes = {Pcm.Length} }}";
}
