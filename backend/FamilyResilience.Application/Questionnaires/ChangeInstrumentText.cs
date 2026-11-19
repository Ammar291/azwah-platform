using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ChangeInstrumentText(
    IInstrumentRepository instruments, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<InstrumentResponse>> HandleAsync(
        Guid instrumentId, ChangeInstrumentTextRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instrument = await instruments.GetAsync(InstrumentId.From(instrumentId), cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<InstrumentResponse>(QuestionnaireErrors.InstrumentNotFound);
        }

        var name = LocalizedText.CreateDraft(request.Name.Ar, request.Name.En, Instrument.MaxNameLength);
        var description = LocalizedText.CreateDraft(request.Description.Ar, request.Description.En, Instrument.MaxDescriptionLength);
        if (name.IsFailure || description.IsFailure)
        {
            return Result.Failure<InstrumentResponse>((name.Error ?? description.Error)!);
        }

        var changed = instrument.ChangeText(name.Value, description.Value, timeProvider.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result.Failure<InstrumentResponse>(changed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument.text_changed", "instrument", instrument.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<InstrumentResponse>(committed.Error!) : Result.Success(instrument.ToResponse());
    }
}
