namespace FamilyResilience.Application.Referrals;

/// <summary>Turns a partner on or off. An inactive partner cannot receive new hand-offs; earlier ones stay as history.</summary>
public sealed record UpdatePartnerRequest(bool? IsActive);
