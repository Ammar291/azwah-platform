using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>An eligibility rule set, parsed and bound to the instrument version and module it targets.</summary>
public sealed record EligibilityRule(
    RuleSetId RuleSetId,
    string RuleCode,
    int RuleVersion,
    string InstrumentCode,
    string? SectionCode,
    InstrumentSectionId? SectionId,
    IEligibilitySpecification Specification)
{
    public static Result<EligibilityRule> From(RuleSet ruleSet, Instrument instrument, InstrumentVersion version)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(version);

        if (ruleSet.InstrumentVersionId != version.Id)
        {
            return Result.Failure<EligibilityRule>(QuestionnaireErrors.RuleSetVersionMismatch);
        }

        var definition = EligibilityDefinitionParser.Parse(ruleSet.Definition);
        if (definition.IsFailure)
        {
            return Result.Failure<EligibilityRule>(definition.Error!);
        }

        var section = RuleSetTarget.Resolve(instrument, version, definition.Value.TargetSectionCode);
        if (section.IsFailure)
        {
            return Result.Failure<EligibilityRule>(section.Error!);
        }

        return Result.Success(new EligibilityRule(
            ruleSet.Id,
            ruleSet.Code,
            ruleSet.VersionNumber,
            instrument.Code,
            definition.Value.TargetSectionCode,
            section.Value?.Id,
            definition.Value.Rule));
    }
}
