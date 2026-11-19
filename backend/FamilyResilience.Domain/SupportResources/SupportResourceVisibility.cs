namespace FamilyResilience.Domain.SupportResources;

/// <summary>Who may find a resource: every signed-in participant, or support staff only.</summary>
public enum SupportResourceVisibility
{
    Everyone,
    StaffOnly,
}
