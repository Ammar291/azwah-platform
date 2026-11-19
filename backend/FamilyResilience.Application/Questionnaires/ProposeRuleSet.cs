using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ProposeRuleSet(
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IRuleSetRepository ruleSets,
    IEnumerable<IRuleDefinitionValidator> validators,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<RuleSetResponse>> HandleAsync(Guid versionId, ProposeRuleSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await QuestionnaireLoads.VersionWithInstrumentAsync(versions, instruments, versionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(loaded.Error!);
        }

        var (version, instrument) = loaded.Value;
        var definition = request.Definition.GetRawText();
        var valid = RuleDefinitions.Validate(validators, request.Kind, definition, instrument, version);
        if (valid.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(valid.Error!);
        }

        var proposed = RuleSet.Propose(request.Kind, request.Code, request.VersionNumber, version, definition, timeProvider.GetUtcNow());
        if (proposed.IsFailure)
        {
            return Result.Failure<RuleSetResponse>(proposed.Error!);
        }

        ruleSets.Add(proposed.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "questionnaires.rule_set.proposed", "rule_set", proposed.Value.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<RuleSetResponse>(committed.Error!) : Result.Success(proposed.Value.ToResponse());
    }
}
