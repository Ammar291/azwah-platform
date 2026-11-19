using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Assistant;

/// <summary>Support the approved rules suggested for a member, by category code, and its referral's status if one was made.</summary>
public sealed record FamilyContextNextStep(
    string Member, string CategoryCode, ReferralStatus? ReferralStatus, DateTimeOffset RecommendedAt);
