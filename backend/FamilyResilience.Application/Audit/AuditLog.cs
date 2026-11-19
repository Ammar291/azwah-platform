using System.Diagnostics;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Audit;

public sealed class AuditLog(
    IAuditEventRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAuditLog
{
    public Result Stage(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var isUser = !record.BySystem && currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.SubjectId);
        var actorType = isUser ? AuditActorType.User
            : record.BySystem || currentUser.IsSystem ? AuditActorType.System
            : AuditActorType.Anonymous;
        var created = AuditEvent.Create(
            timeProvider.GetUtcNow(),
            actorType,
            isUser ? currentUser.SubjectId : null,
            record.Action,
            record.ResourceType,
            record.ResourceId,
            record.Outcome,
            Activity.Current?.TraceId.ToHexString());

        if (created.IsFailure)
        {
            return Result.Failure(created.Error!);
        }

        repository.Add(created.Value);
        return Result.Success();
    }

    public async Task<Result> RecordAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var staged = Stage(record);
        return staged.IsFailure ? staged : await unitOfWork.CommitAsync(cancellationToken);
    }
}
