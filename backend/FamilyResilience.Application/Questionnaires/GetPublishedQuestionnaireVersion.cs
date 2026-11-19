using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>A published version by instrument code and version number. A draft answers "not found", as if it did not exist.</summary>
public sealed class GetPublishedQuestionnaireVersion(IInstrumentRepository instruments, IInstrumentVersionRepository versions)
{
    public async Task<Result<PublishedVersionResponse>> HandleAsync(string code, int versionNumber, CancellationToken cancellationToken)
    {
        var instrument = await instruments.GetByCodeAsync(code, cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<PublishedVersionResponse>(QuestionnaireErrors.InstrumentNotFound);
        }

        var version = await versions.GetByNumberAsync(instrument.Id, versionNumber, cancellationToken);
        return version is not null && version.IsPublished()
            ? Result.Success(version.ToPublished(instrument))
            : Result.Failure<PublishedVersionResponse>(QuestionnaireErrors.VersionNotFound);
    }
}
