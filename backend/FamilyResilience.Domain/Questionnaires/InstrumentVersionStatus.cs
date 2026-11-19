namespace FamilyResilience.Domain.Questionnaires;

/// <summary>Approved is the published state. Approved and retired versions never change.</summary>
public enum InstrumentVersionStatus
{
    Draft = 1,
    Approved = 2,
    Retired = 3,
}
