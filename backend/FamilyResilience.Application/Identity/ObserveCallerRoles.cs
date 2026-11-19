using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// Compares the caller's platform roles with the ones last seen for their subject and audits each difference as
/// <c>identity.role.added</c> or <c>identity.role.removed</c> (resource type <c>platform_role</c>, the role code as the
/// resource), saved with the new snapshot. Roles are granted in the identity provider, so this is how a grant, a removal
/// or a forged role claim becomes visible here. The first request of a subject counts every role as added. When another
/// request recorded the same change at the same moment, the unique subject or the concurrency check stops this one, and
/// nothing more is needed. Returns what it recorded, for the security counters.
/// </summary>
public sealed class ObserveCallerRoles(
    ICurrentUser currentUser,
    IRoleSnapshotRepository snapshots,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const string RoleAdded = "identity.role.added";
    public const string RoleRemoved = "identity.role.removed";
    public const string ResourceType = "platform_role";

    public async Task<Result<ObservedRoleChange>> HandleAsync(CancellationToken cancellationToken)
    {
        var subject = currentUser.SubjectId ?? throw new InvalidOperationException("Roles are observed for a signed-in caller only.");
        var roles = RoleChange.Known(currentUser.Roles);
        var snapshot = await snapshots.GetBySubjectAsync(subject, cancellationToken);
        var change = RoleChange.Between(snapshot?.Roles ?? [], roles);
        if (snapshot is not null && change.IsEmpty)
        {
            return Result.Success(ObservedRoleChange.None);
        }

        var now = timeProvider.GetUtcNow();
        if (snapshot is null)
        {
            var created = RoleSnapshot.Record(subject, roles, now);
            if (created.IsFailure)
            {
                return Result.Failure<ObservedRoleChange>(created.Error!);
            }

            snapshots.Add(created.Value);
        }
        else
        {
            var changed = snapshot.Change(roles, now);
            if (changed.IsFailure)
            {
                return Result.Failure<ObservedRoleChange>(changed.Error!);
            }
        }

        foreach (var (action, role) in change.Added.Select(role => (RoleAdded, role)).Concat(change.Removed.Select(role => (RoleRemoved, role))))
        {
            var staged = auditLog.Stage(new AuditRecord(action, ResourceType, role, AuditOutcome.Succeeded));
            if (staged.IsFailure)
            {
                return Result.Failure<ObservedRoleChange>(staged.Error!);
            }
        }

        var committed = await unitOfWork.CommitAsync(cancellationToken);
        if (committed.Error == PersistenceErrors.Conflict || committed.Error == PersistenceErrors.ConcurrencyConflict)
        {
            return Result.Success(ObservedRoleChange.None);
        }

        return committed.IsFailure
            ? Result.Failure<ObservedRoleChange>(committed.Error!)
            : Result.Success(new ObservedRoleChange(snapshot is null, change));
    }
}
