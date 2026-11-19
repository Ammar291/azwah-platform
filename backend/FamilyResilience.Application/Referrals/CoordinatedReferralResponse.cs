using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// A referral as a support coordinator sees it: the household reference the family can quote, the support category and
/// the hand-off, if any. No answers, scores or member details. Once the member withdraws referral consent the household
/// reference is withheld (null); the hand-off stays, so a coordinator knows which partner to tell.
/// </summary>
public sealed record CoordinatedReferralResponse(
    Guid Id,
    string? HouseholdReference,
    string CategoryCode,
    ReferralStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    HandOffResponse? HandOff);
