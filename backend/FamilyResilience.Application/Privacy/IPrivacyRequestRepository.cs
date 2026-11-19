using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

public interface IPrivacyRequestRepository
{
    void Add(PrivacyRequest request);

    /// <summary>The request, tracked for changes.</summary>
    Task<PrivacyRequest?> GetAsync(PrivacyRequestId id, CancellationToken cancellationToken = default);

    /// <summary>A page of the members' requests, newest first, read without tracking.</summary>
    Task<IReadOnlyList<PrivacyRequest>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);

    /// <summary>The household's open requests, so an erasure can record them as closed before it deletes them.</summary>
    Task<IReadOnlyList<PrivacyRequestId>> ListOpenIdsForHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default);
}
