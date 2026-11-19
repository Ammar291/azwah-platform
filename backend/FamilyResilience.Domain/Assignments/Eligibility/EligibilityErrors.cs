using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Assignments.Eligibility;

public static class EligibilityErrors
{
    public static readonly Error DefinitionInvalid =
        new("eligibility.definition_invalid", "The eligibility definition is invalid.");

    public static readonly Error AmbiguousRules =
        new("eligibility.ambiguous_rules", "More than one approved eligibility rule set targets the same instrument and module.");
}
