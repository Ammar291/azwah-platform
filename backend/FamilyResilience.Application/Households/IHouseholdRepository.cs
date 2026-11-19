using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>Loads and adds households with their rosters. No access checks here: handlers authorise first.</summary>
public interface IHouseholdRepository
{
    void Add(Household household);

    /// <summary>The household with all its members (active and inactive), tracked for changes.</summary>
    Task<Household?> GetAsync(HouseholdId id, CancellationToken cancellationToken = default);

    Task<bool> ReferenceExistsAsync(HouseholdReference reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Households that withdrew before <paramref name="before"/>, oldest first, at most <paramref name="take"/>. A withdrawn
    /// household no longer changes, so its <c>updated_at</c> is the time it withdrew.
    /// </summary>
    Task<IReadOnlyList<WithdrawnHousehold>> ListWithdrawnBeforeAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default);

    /// <summary>The status of the household the member belongs to, or null when the member does not exist.</summary>
    Task<HouseholdStatus?> StatusOfMemberHouseholdAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default);
}
