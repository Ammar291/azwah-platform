using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>
/// Retires an approved rule set so a mistaken approval can be withdrawn. A retired rule set never runs again; results
/// already recorded keep the rule set they name. The retirement time is the server's clock.
/// </summary>
public sealed class RetireRuleSet(
    IRuleSetRepository ruleSets,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<RuleSetResponse>> HandleAsync(Guid ruleSetId, CancellationToken cancellationToken)
    {
        var ruleSet = await ruleSets.GetAsync(RuleSetId.From(ruleSetId), cancellationToken);
        if (ruleSet is null)
        {
            return Result.Failure<RuleSetResponse>(QuestionnaireErrors.RuleSetNotFound);
        }

        var retired = ruleSet.Retire(timeProvider.GetUtcNow());
        if (retired.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(retired.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.rule_set.retired", "rule_set", ruleSet.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<RuleSetResponse>(committed.Error!) : Result.Success(ruleSet.ToResponse());
    }
}
