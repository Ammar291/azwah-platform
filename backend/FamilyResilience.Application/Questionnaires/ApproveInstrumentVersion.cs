using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>Publishes a draft. The approver is the caller's token subject; the request names the source document.</summary>
public sealed class ApproveInstrumentVersion(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<InstrumentVersionResponse>> HandleAsync(
        Guid versionId, ApproveInstrumentVersionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, versionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(loaded.Error!);
        }

        var (version, instrument) = loaded.Value;
        var now = timeProvider.GetUtcNow();
        var approval = Approval.Create(currentUser.SubjectId, now, request.ApprovalSource);
        if (approval.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(approval.Error!);
        }

        var approved = version.Approve(instrument, approval.Value, request.EffectiveFrom, request.EffectiveTo, now);
        if (approved.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(approved.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument_version.approved", "instrument_version", version.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<InstrumentVersionResponse>(committed.Error!)
            : Result.Success(version.ToResponse(instrument));
    }
}
