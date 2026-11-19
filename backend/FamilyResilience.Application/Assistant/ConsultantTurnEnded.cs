namespace FamilyResilience.Application.Assistant;

/// <summary>The consultant stopped speaking (its speech turned to silence); the app plays whatever it still holds back.</summary>
public sealed record ConsultantTurnEnded : VoiceConsultantEvent;
