using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>A system administrator takes an open alert on, audited as <see cref="Acknowledged"/>.</summary>
public sealed class AcknowledgeSafetyAlert(
    ISafetyAlertRepository alerts,
    ISafetyAlertReadModel readModel,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string Acknowledged = "safeguarding.alert.acknowledged";

    public async Task<Result<AdminSafetyAlertResponse>> HandleAsync(Guid alertId, CancellationToken cancellationToken)
    {
        var id = SafetyAlertId.From(alertId);
        var alert = await alerts.GetAsync(id, cancellationToken);
        if (alert is null)
        {
            return Result.Failure<AdminSafetyAlertResponse>(SafetyErrors.AlertNotFound);
        }

        var actor = currentUser.SubjectId ?? throw new InvalidOperationException("The SystemAdministrator policy requires a subject.");
        var acknowledged = alert.Acknowledge(actor, timeProvider.GetUtcNow());
        if (acknowledged.IsFailure)
        {
            return Result.Failure<AdminSafetyAlertResponse>(acknowledged.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, Acknowledged, RecordSafetySignal.ResourceType, alertId, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<AdminSafetyAlertResponse>(committed.Error!);
        }

        return Result.Success(await readModel.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("An acknowledged alert must still exist."));
    }
}
