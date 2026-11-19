using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Consent;

/// <summary>
/// An approved consent policy version. The text lives in the reviewed documents its references point to; until legal
/// review (open question 9) no approved policy exists outside test databases.
/// </summary>
public sealed record ConsentPolicyResponse(
    Guid Id, ConsentType ConsentType, int VersionNumber, string DocumentReferenceAr, string DocumentReferenceEn, DateTimeOffset? ApprovedAt);
