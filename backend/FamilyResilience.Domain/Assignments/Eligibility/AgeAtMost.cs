namespace FamilyResilience.Domain.Assignments.Eligibility;

public sealed record AgeAtMost(int MaximumAge) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Age <= MaximumAge
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.AgeAboveMaximum);
    }
}
