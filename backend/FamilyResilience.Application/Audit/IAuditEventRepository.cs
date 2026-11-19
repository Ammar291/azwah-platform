using FamilyResilience.Domain.Audit;

namespace FamilyResilience.Application.Audit;

/// <summary>Append-only: there is deliberately no update or delete.</summary>
public interface IAuditEventRepository
{
    void Add(AuditEvent auditEvent);
}
