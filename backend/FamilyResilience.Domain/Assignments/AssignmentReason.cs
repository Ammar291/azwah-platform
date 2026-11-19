namespace FamilyResilience.Domain.Assignments;

/// <summary>Every assignment comes from an approved eligibility rule set; a reissue replaces an expired or cancelled one.</summary>
public enum AssignmentReason
{
    EligibilityRule = 1,
    Reissue = 2,
}
