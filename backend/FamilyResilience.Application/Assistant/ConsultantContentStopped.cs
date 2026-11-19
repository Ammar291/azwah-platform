namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The model's provider stopped this session over its content (D-141): OpenAI's own safety filter ends a GPT-Live
/// session when someone speaks of hurting themselves, and cannot be switched off. It is the session's last event; what
/// the consultant was still saying is dropped, never settled. <see cref="ContinuingConsultantSession"/> decides what the
/// person hears next and opens a new session; the app never sees this event.
/// </summary>
public sealed record ConsultantContentStopped : VoiceConsultantEvent;
