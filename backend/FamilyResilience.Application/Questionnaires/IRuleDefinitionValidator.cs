using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>
/// Validates a rule-set definition of one kind against the version it belongs to. The module that owns the rules
/// implements it (Assignments for eligibility, Scoring for scoring), so Questionnaires never parses them itself.
/// </summary>
public interface IRuleDefinitionValidator
{
    RuleSetKind Kind { get; }

    Result Validate(string definition, Instrument instrument, InstrumentVersion version);
}
