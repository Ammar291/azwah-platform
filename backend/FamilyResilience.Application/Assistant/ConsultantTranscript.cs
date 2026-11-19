namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A live caption: what the person or the consultant said so far in this turn, final once the words are settled. Never
/// logged or stored: <see cref="ToString"/> prints the length only.
/// </summary>
public sealed record ConsultantTranscript(ConsultantSpeaker Speaker, string Text, bool Final) : VoiceConsultantEvent
{
    public override string ToString() => $"ConsultantTranscript {{ Speaker = {Speaker}, TextLength = {Text.Length}, Final = {Final} }}";
}
