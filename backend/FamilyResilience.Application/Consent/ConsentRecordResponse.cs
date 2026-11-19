using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Consent;

public sealed record ConsentRecordResponse(
    Guid Id,
    Guid MemberId,
    ConsentType ConsentType,
    Guid PolicyVersionId,
    int PolicyVersionNumber,
    ConsentCaptureMethod CaptureMethod,
    DateTimeOffset GrantedAt,
    DateTimeOffset? WithdrawnAt,
    bool IsActive);
