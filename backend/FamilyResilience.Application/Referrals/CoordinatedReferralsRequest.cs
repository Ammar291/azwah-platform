using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>Query string for the referral queue: an optional status, newest first.</summary>
public sealed class CoordinatedReferralsRequest : IPageRequest
{
    public ReferralStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
