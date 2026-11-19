using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

public sealed class EligibilityDefinitionValidator : IRuleDefinitionValidator
{
    public RuleSetKind Kind => RuleSetKind.Eligibility;

    public Result Validate(string definition, Instrument instrument, InstrumentVersion version)
    {
        var parsed = EligibilityDefinitionParser.Parse(definition);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error!);
        }

        var target = RuleSetTarget.Resolve(instrument, version, parsed.Value.TargetSectionCode);
        return target.IsFailure ? Result.Failure(target.Error!) : Result.Success();
    }
}
