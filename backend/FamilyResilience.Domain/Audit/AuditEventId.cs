using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Audit;

public readonly record struct AuditEventId(Guid Value) : IEntityId<AuditEventId>
{
    /// <summary>Version 7 GUIDs sort by creation time, which keeps the primary-key index compact.</summary>
    public static AuditEventId New() => new(Guid.CreateVersion7());

    public static AuditEventId From(Guid value) => new(value);
}
