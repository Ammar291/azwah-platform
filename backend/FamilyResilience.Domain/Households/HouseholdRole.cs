namespace FamilyResilience.Domain.Households;

/// <summary>What a platform user's membership lets them do in the household. Proposed (open questions 11 and 14).</summary>
public enum HouseholdRole
{
    /// <summary>Manages the roster and may act for active members who have no account of their own.</summary>
    Manager = 1,

    /// <summary>Acts only for the member they are linked to.</summary>
    Member = 2,
}
