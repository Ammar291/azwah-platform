using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// A scoring definition is valid only if its target exists, the engine supports its policy and algorithm version, and
/// that strategy accepts its parameters for exactly the items in scope (directions, scales, bands).
/// </summary>
public sealed class ScoringDefinitionValidator(ScoringStrategyCatalog catalog) : IRuleDefinitionValidator
{
    public RuleSetKind Kind => RuleSetKind.Scoring;

    public Result Validate(string definition, Instrument instrument, InstrumentVersion version)
    {
        var parsed = ScoringDefinitionParser.Parse(definition);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error!);
        }

        var target = RuleSetTarget.Resolve(instrument, version, parsed.Value.TargetSectionCode);
        if (target.IsFailure)
        {
            return Result.Failure(target.Error!);
        }

        var content = ScoringContent.From(version, parsed.Value.TargetSectionCode);
        if (content.IsFailure)
        {
            return Result.Failure(content.Error!);
        }

        var strategy = catalog.Resolve(parsed.Value.PolicyCode, parsed.Value.AlgorithmVersion);
        if (strategy.IsFailure)
        {
            return Result.Failure(strategy.Error!);
        }

        var parameters = strategy.Value.ValidateParameters(parsed.Value.Parameters, content.Value);
        return parameters.IsFailure ? Result.Failure(parameters.Error!) : Result.Success();
    }
}
