using FamilyResilience.Domain.Privacy;

namespace FamilyResilience.Application.Privacy;

/// <summary>A privacy request as the family sees it.</summary>
public sealed record PrivacyRequestResponse(
    Guid Id,
    Guid MemberId,
    PrivacyRequestType Type,
    PrivacyRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    PrivacyRequestResolution? Resolution);
