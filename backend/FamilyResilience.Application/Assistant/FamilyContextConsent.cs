using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Assistant;

/// <summary>A consent decision of a member: its purpose and whether it is still active.</summary>
public sealed record FamilyContextConsent(
    string Member, ConsentType Purpose, bool IsActive, DateTimeOffset GrantedAt, DateTimeOffset? WithdrawnAt);
