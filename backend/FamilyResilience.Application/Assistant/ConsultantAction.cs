namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The consultant offers a place in the app (an <see cref="AssistantScreens"/> code); the app shows it as a button
/// (D-124). Sent when the delegated agent calls <c>suggest_app_screen</c> or the safety check flags danger.
/// </summary>
public sealed record ConsultantAction(string Action) : VoiceConsultantEvent;
