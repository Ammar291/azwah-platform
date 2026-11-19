using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Safeguarding;

/// <summary>
/// Who an alert is about: always a user or a household, with the member when one is known. A member comes with their
/// household (<c>ck_safety_alerts_member_household</c>).
/// </summary>
public sealed record SafetyAlertSubject
{
    public SafetyAlertSubject(PlatformUserId? userId, HouseholdId? householdId, FamilyMemberId? memberId)
    {
        if (userId is null && householdId is null)
        {
            throw new ArgumentException("An alert subject needs a user or a household.", nameof(userId));
        }

        if (memberId is not null && householdId is null)
        {
            throw new ArgumentException("An alert subject with a member needs the member's household.", nameof(householdId));
        }

        UserId = userId;
        HouseholdId = householdId;
        MemberId = memberId;
    }

    public PlatformUserId? UserId { get; }

    public HouseholdId? HouseholdId { get; }

    public FamilyMemberId? MemberId { get; }
}
