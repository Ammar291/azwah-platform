namespace FamilyResilience.Domain.Audit;

public enum AuditActorType
{
    /// <summary>A signed-in person, identified by the token subject.</summary>
    User = 1,

    /// <summary>The platform itself, for example a scheduled job.</summary>
    System = 2,

    /// <summary>A caller with no identity, for example a health probe.</summary>
    Anonymous = 3,
}
