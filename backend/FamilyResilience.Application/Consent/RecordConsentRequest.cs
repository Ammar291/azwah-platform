namespace FamilyResilience.Application.Consent;

/// <summary>
/// A member's consent decision. The policy version names the purpose (participation, research use or referral); a grant
/// must name the current approved version, and a withdrawal withdraws whatever version of that purpose is active.
/// </summary>
public sealed record RecordConsentRequest(Guid MemberId, Guid PolicyVersionId, ConsentDecision? Decision);
