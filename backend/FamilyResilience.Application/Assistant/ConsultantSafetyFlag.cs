using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The model session's own detectors flagged what the person said (D-135): <see cref="SafetyDetector.AiModeration"/>
/// from the safety check on a final line, or <see cref="SafetyDetector.ProviderFilter"/> from the provider's content
/// filter. The session emits it just before the SOS <see cref="ConsultantAction"/> it offers for the same reason. The
/// relay records it for staff and tells the app with the safety message; the flag itself never reaches the app. Fixed
/// codes only: it never carries what was said.
/// </summary>
public sealed record ConsultantSafetyFlag(SafetyConcern Concern, SafetyDetector Detector) : VoiceConsultantEvent;
