using FamilyResilience.Domain.Audit;

namespace FamilyResilience.Application.Audit;

public sealed record AuditRecord(string Action, string ResourceType, string? ResourceId, AuditOutcome Outcome)
{
    /// <summary>
    /// True when the platform itself acted (a detector, not the person whose request it ran in): the event names the
    /// System actor and no subject, even inside a signed-in request. Staff and participant actions never set it.
    /// </summary>
    public bool BySystem { get; init; }
}
