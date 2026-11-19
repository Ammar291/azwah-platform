namespace FamilyResilience.Domain.Safeguarding;

/// <summary>Where the signal came from. Wire codes: assistant_chat, consultant_voice, support_search, household_assessment.</summary>
public enum SafetyAlertSource
{
    AssistantChat = 1,
    ConsultantVoice = 2,
    SupportSearch = 3,
    HouseholdAssessment = 4,
}
