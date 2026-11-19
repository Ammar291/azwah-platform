using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>Every rule set of one version, for the read-only configuration view.</summary>
public sealed class ListVersionRuleSets(IInstrumentVersionRepository versions, IRuleSetRepository ruleSets)
{
    public async Task<Result<IReadOnlyList<RuleSetResponse>>> HandleAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var id = InstrumentVersionId.From(versionId);
        if (!await versions.ExistsAsync(id, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<RuleSetResponse>>(QuestionnaireErrors.VersionNotFound);
        }

        var listed = await ruleSets.ListForVersionAsync(id, cancellationToken);
        return Result.Success<IReadOnlyList<RuleSetResponse>>(listed.Select(ruleSet => ruleSet.ToResponse()).ToList());
    }
}
