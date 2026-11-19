using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Sessions;

public static class DemoToolsErrors
{
    /// <summary>Demonstration tools are switched off on this server, so their endpoint does not exist here (404).</summary>
    public static readonly Error Disabled =
        new("demo_tools.disabled", "Demonstration tools are not available.");

    /// <summary>A new check-in (D-144) waits for the roster to be confirmed, as the household's first one did (409).</summary>
    public static readonly Error RosterNotConfirmed =
        new("demo_tools.roster_not_confirmed", "Confirm the household's roster before opening a new check-in.");

    /// <summary>No approved eligibility rule gives anyone the caller may act for the household assessment, so no round is made (409).</summary>
    public static readonly Error NothingToAssign =
        new("demo_tools.nothing_to_assign", "No approved eligibility rule assigns this household the household assessment.");
}
