using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// Support the approved rules suggested after a check-in, as a category code clients translate. The referral fields are
/// set only when the member had referral consent; no partner organisation is involved.
/// </summary>
public sealed record RecommendationResponse(
    Guid Id,
    Guid MemberId,
    Guid SessionId,
    string CategoryCode,
    DateTimeOffset CreatedAt,
    Guid? ReferralId,
    ReferralStatus? ReferralStatus);
