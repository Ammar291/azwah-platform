using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>The member's employment status is one of <see cref="Statuses"/>; unknown when the status is not recorded.</summary>
public sealed record EmploymentStatusIn(IReadOnlySet<EmploymentStatus> Statuses) : IEligibilitySpecification
{
    public SpecificationResult Evaluate(EligibilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.EmploymentStatus is not { } status)
        {
            return SpecificationResult.Unknown([MissingFacts.EmploymentStatus]);
        }

        return Statuses.Contains(status)
            ? SpecificationResult.Satisfied
            : SpecificationResult.NotSatisfied(EligibilityReasons.EmploymentStatusNotQualifying);
    }
}
