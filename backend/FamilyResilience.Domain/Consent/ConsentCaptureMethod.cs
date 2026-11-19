namespace FamilyResilience.Domain.Consent;

/// <summary>How consent was captured. Proposed list, not yet approved (docs/decisions.md).</summary>
public enum ConsentCaptureMethod
{
    InApp = 1,
    PaperForm = 2,
    AssistedByStaff = 3,
}
