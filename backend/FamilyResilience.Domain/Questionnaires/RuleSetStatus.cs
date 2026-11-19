namespace FamilyResilience.Domain.Questionnaires;

/// <summary>Only Approved rule sets may be used. Approved and retired rule sets never change.</summary>
public enum RuleSetStatus
{
    Proposed = 1,
    Approved = 2,
    Retired = 3,
}
