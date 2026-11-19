using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Consent;

public interface IConsentRecordRepository
{
    void Add(ConsentRecord consentRecord);

    Task<ConsentRecord?> GetAsync(ConsentRecordId id, CancellationToken cancellationToken = default);

    Task<ConsentRecord?> GetActiveAsync(
        FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the member's active consent of <paramref name="consentType"/> and holds a <c>FOR SHARE</c> row lock until
    /// the surrounding transaction ends, so a concurrent withdrawal waits. Call it inside
    /// <c>IUnitOfWork.ExecuteInTransactionAsync</c>; outside one it throws.
    /// </summary>
    Task<ConsentRecord?> GetActiveWithShareLockAsync(
        FamilyMemberId memberId, ConsentType consentType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConsentRecord>> ListForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default);

    /// <summary>One page of the members' consent records, newest grant first.</summary>
    Task<IReadOnlyList<ConsentRecord>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);
}
