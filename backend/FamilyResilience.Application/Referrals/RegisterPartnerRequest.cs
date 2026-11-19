namespace FamilyResilience.Application.Referrals;

/// <summary>
/// A partner organisation: a snake-case code, its name in Arabic and English, and the support category codes it takes,
/// as referral rule sets name them (not checked against the rule sets).
/// </summary>
public sealed record RegisterPartnerRequest(string? Code, string? NameAr, string? NameEn, IReadOnlyList<string>? Categories);
