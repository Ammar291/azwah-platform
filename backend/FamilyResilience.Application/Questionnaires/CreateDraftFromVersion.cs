using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class CreateDraftFromVersion(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<InstrumentVersionResponse>> HandleAsync(Guid sourceVersionId, CancellationToken cancellationToken)
    {
        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, sourceVersionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(loaded.Error!);
        }

        var (source, instrument) = loaded.Value;
        var next = await versions.GetHighestVersionNumberAsync(instrument.Id, cancellationToken) + 1;
        var draft = InstrumentVersion.CreateDraftFrom(instrument, source, next, timeProvider.GetUtcNow());
        if (draft.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(draft.Error!);
        }

        versions.Add(draft.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument_version.drafted", "instrument_version", draft.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<InstrumentVersionResponse>(committed.Error!)
            : Result.Success(draft.Value.ToResponse(instrument));
    }
}
