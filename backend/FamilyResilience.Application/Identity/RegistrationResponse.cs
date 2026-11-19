namespace FamilyResilience.Application.Identity;

/// <summary>When the caller's platform user was created (UTC).</summary>
public sealed record RegistrationResponse(DateTimeOffset RegisteredAt);
