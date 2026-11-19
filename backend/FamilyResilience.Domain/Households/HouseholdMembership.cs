using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Households;

/// <summary>
/// Links a platform user to the family member they are, with their role in the household. Part of the
/// <see cref="Household"/> aggregate. A user has at most one active membership, and a member at most one.
/// </summary>
public sealed class HouseholdMembership : Entity<HouseholdMembershipId>
{
    private HouseholdMembership(HouseholdMembershipId id)
        : base(id)
    {
    }

    public HouseholdId HouseholdId { get; private set; }

    public PlatformUserId PlatformUserId { get; private set; }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public HouseholdRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    internal static HouseholdMembership Create(
        HouseholdId householdId, PlatformUserId userId, FamilyMemberId memberId, HouseholdRole role, DateTimeOffset now) =>
        new(HouseholdMembershipId.New())
        {
            HouseholdId = householdId,
            PlatformUserId = userId,
            FamilyMemberId = memberId,
            Role = role,
            Status = MembershipStatus.Active,
            CreatedAt = now,
        };

    internal void End(DateTimeOffset now)
    {
        Status = MembershipStatus.Ended;
        EndedAt = now;
    }
}
