namespace FamilyResilience.Domain.Assignments.Eligibility;

public sealed record IsHeadOfHousehold : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.IsHeadOfHousehold
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.NotHeadOfHousehold);
    }
}
