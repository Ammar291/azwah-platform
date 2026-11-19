using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>Untracked reads of the referral queue for support coordinators. Implemented in Infrastructure.</summary>
public interface IReferralCoordinationReadModel
{
    /// <summary>Referrals, optionally of one status, newest first.</summary>
    Task<IReadOnlyList<CoordinatedReferralResponse>> ListAsync(
        ReferralStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(ReferralStatus? status, CancellationToken cancellationToken = default);

    Task<CoordinatedReferralResponse?> GetAsync(SupportReferralId id, CancellationToken cancellationToken = default);
}
