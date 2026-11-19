namespace FamilyResilience.Application.Identity;

/// <summary>
/// The caller confirms, once, that the details their identity provider showed them are theirs. The details themselves
/// are never sent: the platform keeps none of them (D-85, D-97).
/// </summary>
public sealed record RegistrationRequest(bool? DetailsConfirmed);
