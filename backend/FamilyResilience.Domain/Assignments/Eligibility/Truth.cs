namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>Three-valued logic: a rule can be true, false, or unknown because a fact is missing.</summary>
public enum Truth
{
    False = 0,
    True = 1,
    Unknown = 2,
}
