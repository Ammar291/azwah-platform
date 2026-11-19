namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>At least one rule holds. A true rule decides the result even when another rule lacks information.</summary>
public sealed record AnyOf(IReadOnlyList<IEligibilitySpecification> Rules) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        var results = Rules.Select(rule => rule.Evaluate(facts)).ToList();
        if (results.Exists(result => result.Truth == Truth.True))
        {
            return SpecificationResult.Satisfied;
        }

        var unknown = results.Where(result => result.Truth == Truth.Unknown).ToList();
        return unknown.Count == 0
            ? SpecificationResult.NotSatisfied(EligibilityReasons.NoneOfAlternativesMet)
            : SpecificationResult.UnknownFrom(unknown);
    }
}
