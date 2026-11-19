namespace FamilyResilience.IdentityProvider;

/// <summary>A fixed test identity. Subjects never change, so platform records made with one survive restarts.</summary>
internal sealed record Persona(string Code, string Subject, IReadOnlyList<string> Roles, string Description);
