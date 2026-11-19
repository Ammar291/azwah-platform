namespace FamilyResilience.IdentityProvider;

/// <summary>The personas anyone may sign in as. No names or other personal claims: only a subject and roles.</summary>
internal static class Personas
{
    public static IReadOnlyList<Persona> All { get; } =
    [
        new("participant_lead", "dev-participant-lead", ["participant"], "Participant who leads a household"),
        new("participant_member", "dev-participant-member", ["participant"], "Participant who is a household member"),
        new("participant_new", "dev-participant-new", ["participant"], "Participant with no household yet"),
        new("researcher", "dev-researcher", ["researcher"], "Researcher"),
        new("program_administrator", "dev-program-administrator", ["program_administrator"], "Programme administrator"),
        new("system_administrator", "dev-system-administrator", ["system_administrator"], "System administrator"),
        new("support_coordinator", "dev-support-coordinator", ["support_coordinator"], "Support coordinator"),
    ];

    public static Persona? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(persona => string.Equals(persona.Code, code, StringComparison.Ordinal));
}
