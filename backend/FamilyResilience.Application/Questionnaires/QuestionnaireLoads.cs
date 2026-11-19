using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

internal static class QuestionnaireLoads
{
    /// <summary>The version (404 if unknown) and its instrument, which the restrictive foreign key guarantees exists.</summary>
    public static async Task<Result<(InstrumentVersion Version, Instrument Instrument)>> VersionWithInstrumentAsync(
        IInstrumentVersionRepository versions, IInstrumentRepository instruments, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await versions.GetAsync(InstrumentVersionId.From(versionId), cancellationToken);
        if (version is null)
        {
            return Result.Failure<(InstrumentVersion, Instrument)>(QuestionnaireErrors.VersionNotFound);
        }

        var instrument = await instruments.GetAsync(version.InstrumentId, cancellationToken)
            ?? throw new InvalidOperationException("A version's instrument must exist.");
        return Result.Success((version, instrument));
    }
}
