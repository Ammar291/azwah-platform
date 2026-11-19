namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Something a live model session produced: <see cref="ConsultantAudio"/>, <see cref="ConsultantTranscript"/>,
/// <see cref="ConsultantTurnEnded"/>, <see cref="ConsultantInterrupted"/>, <see cref="ConsultantAction"/> or
/// <see cref="ConsultantSafetyFlag"/>. The server relays each to the app as it comes (a flag becomes the safety message,
/// never a message of its own) and keeps none of them.
/// </summary>
public abstract record VoiceConsultantEvent;
