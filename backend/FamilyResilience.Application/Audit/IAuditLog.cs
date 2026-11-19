using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Audit;

public interface IAuditLog
{
    /// <summary>
    /// Adds an audit event for the current caller without committing, so it is saved in the same transaction as the
    /// change it describes. Use this inside a use case.
    /// </summary>
    Result Stage(AuditRecord record);

    /// <summary>
    /// Stages and commits an audit event on its own. Use it only for standalone events (reads, denied access, a failed
    /// outcome recorded after a rollback) in a scope with no other pending changes.
    /// </summary>
    Task<Result> RecordAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
