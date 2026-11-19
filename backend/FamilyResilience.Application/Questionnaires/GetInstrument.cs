using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class GetInstrument(IInstrumentRepository instruments)
{
    public async Task<Result<InstrumentResponse>> HandleAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        await instruments.GetAsync(InstrumentId.From(instrumentId), cancellationToken) is { } instrument
            ? Result.Success(instrument.ToResponse())
            : Result.Failure<InstrumentResponse>(QuestionnaireErrors.InstrumentNotFound);
}
