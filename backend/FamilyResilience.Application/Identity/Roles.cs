namespace FamilyResilience.Application.Identity;

/// <summary>
/// Proposed platform roles (open question 14 in docs/decisions.md). Role claims are trusted only from validated
/// tokens; never from request payloads. A role alone never opens a participant's data: resource access comes from the
/// platform's own records (household memberships).
/// </summary>
public static class Roles
{
    public const string Participant = "participant";
    public const string Researcher = "researcher";
    public const string SupportCoordinator = "support_coordinator";
    public const string ProgramAdministrator = "program_administrator";
    public const string SystemAdministrator = "system_administrator";

    public static IReadOnlyList<string> All { get; } =
        [Participant, Researcher, SupportCoordinator, ProgramAdministrator, SystemAdministrator];
}
