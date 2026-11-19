namespace FamilyResilience.Domain.Assignments.Eligibility;

public enum EligibilityOutcome
{
    Eligible = 1,
    Ineligible = 2,

    /// <summary>A missing roster fact could change the result, so the engine does not decide.</summary>
    Indeterminate = 3,
}
