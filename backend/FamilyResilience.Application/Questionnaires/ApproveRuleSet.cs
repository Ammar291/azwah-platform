using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>
/// Approves a proposed rule set after validating its definition again against the now-approved version, so an approval
/// always covers a definition the engine can run. The caller is recorded as the approver.
/// </summary>
public sealed class ApproveRuleSet(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IRuleSetRepository ruleSets,
    IEnumerable<IRuleDefinitionValidator> validators,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<RuleSetResponse>> HandleAsync(Guid ruleSetId, ApproveRuleSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ruleSet = await ruleSets.GetAsync(RuleSetId.From(ruleSetId), cancellationToken);
        if (ruleSet is null)
        {
            return Result.Failure<RuleSetResponse>(QuestionnaireErrors.RuleSetNotFound);
        }

        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, ruleSet.InstrumentVersionId.Value, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(loaded.Error!);
        }

        var (version, instrument) = loaded.Value;
        var valid = RuleDefinitions.Validate(validators, ruleSet.Kind, ruleSet.Definition, instrument, version);
        if (valid.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(valid.Error!);
        }

        var now = timeProvider.GetUtcNow();
        var approval = Approval.Create(currentUser.SubjectId, now, request.ApprovalSource);
        if (approval.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(approval.Error!);
        }

        var approved = ruleSet.Approve(approval.Value, version, now);
        if (approved.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(approved.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.rule_set.approved", "rule_set", ruleSet.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<RuleSetResponse>(committed.Error!) : Result.Success(ruleSet.ToResponse());
    }
}
