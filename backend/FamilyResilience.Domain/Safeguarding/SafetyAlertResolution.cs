namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// How staff closed an alert. Wire codes: family_contacted, referred_to_service, emergency_services_contacted,
/// no_action_needed, false_alarm, unable_to_contact.
/// </summary>
public enum SafetyAlertResolution
{
    FamilyContacted = 1,
    ReferredToService = 2,
    EmergencyServicesContacted = 3,
    NoActionNeeded = 4,
    FalseAlarm = 5,
    UnableToContact = 6,
}
