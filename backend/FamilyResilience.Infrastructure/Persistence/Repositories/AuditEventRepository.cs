using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class AuditEventRepository(FamilyResilienceDbContext dbContext) : IAuditEventRepository
{
    public void Add(AuditEvent auditEvent) => dbContext.AuditEvents.Add(auditEvent);
}
