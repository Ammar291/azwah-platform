namespace FamilyResilience.Domain.Privacy;

/// <summary>How a request was closed.</summary>
public enum PrivacyRequestResolution
{
    Completed = 1,
    Declined = 2,
    Withdrawn = 3,
}
