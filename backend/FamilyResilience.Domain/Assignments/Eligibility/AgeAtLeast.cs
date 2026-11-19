namespace FamilyResilience.Domain.Assignments.Eligibility;

public sealed record AgeAtLeast(int MinimumAge) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Age >= MinimumAge
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.AgeBelowMinimum);
    }
}
