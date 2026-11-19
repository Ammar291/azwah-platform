namespace FamilyResilience.Domain.Households;

public enum MembershipStatus
{
    Active = 1,

    /// <summary>No longer grants access. The row stays as history.</summary>
    Ended = 2,
}
