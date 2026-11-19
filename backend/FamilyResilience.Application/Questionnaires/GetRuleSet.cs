using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class GetRuleSet(IRuleSetRepository ruleSets)
{
    public async Task<Result<RuleSetResponse>> HandleAsync(Guid ruleSetId, CancellationToken cancellationToken) =>
        await ruleSets.GetAsync(RuleSetId.From(ruleSetId), cancellationToken) is { } ruleSet
            ? Result.Success(ruleSet.ToResponse())
            : Result.Failure<RuleSetResponse>(QuestionnaireErrors.RuleSetNotFound);
}
