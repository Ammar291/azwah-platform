using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class CreateInstrument(
    IInstrumentRepository instruments, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<InstrumentResponse>> HandleAsync(CreateInstrumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await instruments.GetByCodeAsync(request.Code, cancellationToken) is not null)
        {
            return Result.Failure<InstrumentResponse>(QuestionnaireErrors.InstrumentCodeTaken);
        }

        var created = InstrumentContentImporter.CreateInstrument(request, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return Result.Failure<InstrumentResponse>(created.Error!);
        }

        instruments.Add(created.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.instrument.created", "instrument", created.Value.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<InstrumentResponse>(committed.Error!) : Result.Success(created.Value.ToResponse());
    }
}
