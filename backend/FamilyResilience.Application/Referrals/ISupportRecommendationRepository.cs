using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Referrals;

public interface ISupportRecommendationRepository
{
    /// <summary>Commit it after the session's move to Scored, like a referral.</summary>
    void Add(SupportRecommendation recommendation);

    Task<IReadOnlyList<SupportRecommendation>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>One page of the members' recommendations, newest first.</summary>
    Task<IReadOnlyList<SupportRecommendation>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForMembersAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);
}
