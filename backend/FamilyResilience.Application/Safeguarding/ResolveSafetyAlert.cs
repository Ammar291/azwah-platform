using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// A system administrator closes an open or acknowledged alert with its outcome, audited as <see cref="Resolved"/>. The
/// outcome is a fixed code: the platform keeps no notes about the family.
/// </summary>
public sealed class ResolveSafetyAlert(
    ISafetyAlertRepository alerts,
    ISafetyAlertReadModel readModel,
    ICurrentUser currentUser,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string Resolved = "safeguarding.alert.resolved";

    public async Task<Result<AdminSafetyAlertResponse>> HandleAsync(
        Guid alertId, ResolveSafetyAlertRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = SafetyAlertId.From(alertId);
        var alert = await alerts.GetAsync(id, cancellationToken);
        if (alert is null)
        {
            return Result.Failure<AdminSafetyAlertResponse>(SafetyErrors.AlertNotFound);
        }

        var resolution = request.Resolution ?? throw new InvalidOperationException("The validator requires a resolution.");
        var actor = currentUser.SubjectId ?? throw new InvalidOperationException("The SystemAdministrator policy requires a subject.");
        var resolved = alert.Resolve(resolution, actor, timeProvider.GetUtcNow());
        if (resolved.IsFailure)
        {
            return Result.Failure<AdminSafetyAlertResponse>(resolved.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, Resolved, RecordSafetySignal.ResourceType, alertId, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<AdminSafetyAlertResponse>(committed.Error!);
        }

        return Result.Success(await readModel.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("A resolved alert must still exist."));
    }
}
