namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>Every rule holds. A false rule decides the result even when another rule lacks information.</summary>
public sealed record AllOf(IReadOnlyList<IEligibilitySpecification> Rules) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        var results = Rules.Select(rule => rule.Evaluate(facts)).ToList();
        var firstFalse = results.Find(result => result.Truth == Truth.False);
        if (firstFalse is not null)
        {
            return firstFalse;
        }

        var unknown = results.Where(result => result.Truth == Truth.Unknown).ToList();
        return unknown.Count == 0 ? SpecificationResult.Satisfied : SpecificationResult.UnknownFrom(unknown);
    }
}
