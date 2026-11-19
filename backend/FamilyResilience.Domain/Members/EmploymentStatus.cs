namespace FamilyResilience.Domain.Members;

/// <summary>Proposed list, not yet approved (docs/decisions.md). Which values count as "employed" is an eligibility rule.</summary>
public enum EmploymentStatus
{
    Employed = 1,
    SelfEmployed = 2,
    Unemployed = 3,
    Student = 4,
    Homemaker = 5,
    Retired = 6,
    Other = 7,
}
