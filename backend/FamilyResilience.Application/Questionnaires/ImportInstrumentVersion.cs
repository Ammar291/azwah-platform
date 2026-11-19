using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ImportInstrumentVersion(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<InstrumentVersionResponse>> HandleAsync(
        Guid instrumentId, InstrumentContentDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var instrument = await instruments.GetAsync(InstrumentId.From(instrumentId), cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<InstrumentVersionResponse>(QuestionnaireErrors.InstrumentNotFound);
        }

        var next = await versions.GetHighestVersionNumberAsync(instrument.Id, cancellationToken) + 1;
        var draft = InstrumentContentImporter.BuildDraft(instrument, next, document, timeProvider.GetUtcNow());
        if (draft.IsFailure)
        {
            return Result.Failure<InstrumentVersionResponse>(draft.Error!);
        }

        versions.Add(draft.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument_version.imported", "instrument_version", draft.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<InstrumentVersionResponse>(committed.Error!)
            : Result.Success(draft.Value.ToResponse(instrument));
    }
}
