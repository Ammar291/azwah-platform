using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Audit;

/// <summary>Stages one audit event describing the change and commits both in the same transaction.</summary>
internal static class AuditedCommit
{
    public static Task<Result> RunAsync(
        IAuditLog auditLog,
        IUnitOfWork unitOfWork,
        string action,
        string resourceType,
        Guid resourceId,
        CancellationToken cancellationToken) =>
        RunAsync(
            auditLog,
            unitOfWork,
            new AuditRecord(action, resourceType, resourceId.ToString(), AuditOutcome.Succeeded),
            cancellationToken);

    public static async Task<Result> RunAsync(
        IAuditLog auditLog, IUnitOfWork unitOfWork, AuditRecord record, CancellationToken cancellationToken)
    {
        var staged = auditLog.Stage(record);
        return staged.IsFailure ? staged : await unitOfWork.CommitAsync(cancellationToken);
    }
}
