namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>An active member of the household (any relationship) is younger than <see cref="Age"/>.</summary>
public sealed record HouseholdHasMemberYoungerThan(int Age) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.ActiveHouseholdMemberAges.Any(age => age < Age)
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.NoHouseholdMemberUnderAge);
    }
}
