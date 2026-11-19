namespace FamilyResilience.Domain.Members;

public enum MemberStatus
{
    Active = 1,

    /// <summary>Removed from the roster. The row and its assessment history stay.</summary>
    Inactive = 2,
}
