namespace FamilyResilience.Domain.Assignments.Eligibility;

public sealed record IsPrimaryIncomeEarner : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.IsPrimaryIncomeEarner
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.NotPrimaryIncomeEarner);
    }
}
