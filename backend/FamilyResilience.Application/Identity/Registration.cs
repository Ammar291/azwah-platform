namespace FamilyResilience.Application.Identity;

/// <summary>The caller's registration, and whether this request created it.</summary>
public sealed record Registration(RegistrationResponse Response, bool Created);
