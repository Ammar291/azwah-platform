using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>A privacy request as a program administrator sees it: with the household reference the family can quote.</summary>
public sealed record AdminPrivacyRequestResponse(
    Guid Id,
    string HouseholdReference,
    Guid MemberId,
    PrivacyRequestType Type,
    PrivacyRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    PrivacyRequestResolution? Resolution);
