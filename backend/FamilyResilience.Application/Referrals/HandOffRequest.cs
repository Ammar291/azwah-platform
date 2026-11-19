namespace FamilyResilience.Application.Referrals;

/// <summary>The partner organisation a support coordinator handed the referral to, outside the platform.</summary>
public sealed record HandOffRequest(Guid? PartnerId);
