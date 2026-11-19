using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Questionnaires;

public sealed class GetInstrumentVersion(IInstrumentRepository instruments, IInstrumentVersionRepository versions)
{
    public async Task<Result<InstrumentVersionResponse>> HandleAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, versionId, cancellationToken);
        return loaded.IsFailure
            ? Result.Failure<InstrumentVersionResponse>(loaded.Error!)
            : Result.Success(loaded.Value.Version.ToResponse(loaded.Value.Instrument));
    }
}
