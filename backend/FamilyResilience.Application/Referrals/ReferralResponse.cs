using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// A consented referral recorded on the platform. <c>handOff</c> is set once a support coordinator has handed it to a
/// partner organisation, always manually and outside the platform; the platform itself sends the partner nothing.
/// </summary>
public sealed record ReferralResponse(
    Guid Id,
    Guid MemberId,
    Guid SessionId,
    string CategoryCode,
    ReferralStatus Status,
    string? AssignedServiceCode,
    string? ResolutionCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    HandOffResponse? HandOff);
