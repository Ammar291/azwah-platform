using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// <c>resolution</c>: family_contacted, referred_to_service, emergency_services_contacted, no_action_needed, false_alarm
/// or unable_to_contact. Required.
/// </summary>
public sealed record ResolveSafetyAlertRequest(SafetyAlertResolution? Resolution);
