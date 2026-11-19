using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// What the signed-in caller may reach, decided from the validated token's subject and the platform's own records
/// (their household membership), never from IDs or roles in the request. Answers are cached for the request.
/// </summary>
public interface ICallerAccess
{
    /// <summary>The caller's platform user and household, or null when the caller has not enrolled.</summary>
    Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether the caller is the active manager of the household.</summary>
    Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default);

    /// <summary>Whether the caller may act for the member: their own, or one their managed household allows (D-49).</summary>
    Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default);

    /// <summary>Every member the caller may act for; empty when the caller has not enrolled.</summary>
    Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default);
}
