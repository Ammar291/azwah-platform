using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Questionnaires;

public sealed class RetireInstrumentVersion(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<InstrumentVersionResponse>> HandleAsync(
        Guid versionId, EffectiveToRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, versionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(loaded.Error!);
        }

        var (version, instrument) = loaded.Value;

        var retired = version.Retire(request.EffectiveTo, timeProvider.GetUtcNow());
        if (retired.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(retired.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument_version.retired", "instrument_version", version.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<InstrumentVersionResponse>(committed.Error!)
            : Result.Success(version.ToResponse(instrument));
    }
}
