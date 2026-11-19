using FamilyResilience.Domain.Consent;

namespace FamilyResilience.Application.Consent;

internal static class ConsentMappings
{
    public static ConsentPolicyResponse ToResponse(this ConsentPolicyVersion policy) =>
        new(policy.Id.Value, policy.ConsentType, policy.VersionNumber, policy.DocumentReferenceAr, policy.DocumentReferenceEn, policy.ApprovedAt);

    public static ConsentRecordResponse ToResponse(this ConsentRecord record, int policyVersionNumber) =>
        new(
            record.Id.Value,
            record.FamilyMemberId.Value,
            record.ConsentType,
            record.ConsentPolicyVersionId.Value,
            policyVersionNumber,
            record.CaptureMethod,
            record.GrantedAt,
            record.WithdrawnAt,
            record.IsActive);
}
