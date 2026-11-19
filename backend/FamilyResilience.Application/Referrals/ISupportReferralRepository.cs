using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Referrals;

public interface ISupportReferralRepository
{
    /// <summary>
    /// Adds a referral. Commit it after the session's move to Scored, inside the same
    /// <c>IUnitOfWork.ExecuteInTransactionAsync</c>: the database refuses a referral whose session is not yet Scored.
    /// </summary>
    void Add(SupportReferral referral);

    Task<SupportReferral?> GetAsync(SupportReferralId id, CancellationToken cancellationToken = default);

    /// <summary>Every referral that followed from the session, oldest first, cancelled ones included.</summary>
    Task<IReadOnlyList<SupportReferral>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The open and in-progress referrals that rely on the consent. Call it after the withdrawal has committed, inside
    /// the same transaction, so a referral that committed while the withdrawal waited is seen (D-45).
    /// </summary>
    Task<IReadOnlyList<SupportReferral>> ListLiveForConsentAsync(ConsentRecordId consentId, CancellationToken cancellationToken = default);

    /// <summary>One page of the members' referrals, newest first, read without tracking.</summary>
    Task<IReadOnlyList<SupportReferral>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);

    /// <summary>The referrals with these IDs, read without tracking.</summary>
    Task<IReadOnlyList<SupportReferral>> ListByIdsAsync(IReadOnlyCollection<SupportReferralId> ids, CancellationToken cancellationToken = default);
}
