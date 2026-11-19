namespace FamilyResilience.Domain.Households;

public enum HouseholdStatus
{
    Active = 1,

    /// <summary>The household left the study. Its history is kept until the retention rules say otherwise.</summary>
    Withdrawn = 2,
}
