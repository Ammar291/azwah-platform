namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>One rule of an eligibility definition. Pure: the same facts always give the same result.</summary>
public interface IEligibilitySpecification
{
    SpecificationResult Evaluate(EligibilityFacts facts);
}
