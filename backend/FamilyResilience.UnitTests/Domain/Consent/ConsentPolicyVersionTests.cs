using FamilyResilience.Domain.Consent;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Consent;

public sealed class ConsentPolicyVersionTests
{
    [Fact]
    public void Draft_policy_records_its_type_version_and_document_references()
    {
        var policy = Ok(ConsentPolicyVersion.CreateDraft(ConsentType.Referral, 2, " consent/ar ", "consent/en", Now));

        Assert.Equal(ConsentType.Referral, policy.ConsentType);
        Assert.Equal(2, policy.VersionNumber);
        Assert.Equal(ConsentPolicyStatus.Draft, policy.Status);
        Assert.Equal("consent/ar", policy.DocumentReferenceAr);
        Assert.Null(policy.ApprovedAt);
    }

    [Fact]
    public void Version_number_must_be_positive() =>
        Assert.Equal(
            ConsentErrors.VersionNumberInvalid,
            ConsentPolicyVersion.CreateDraft(ConsentType.Referral, 0, "ar", "en", Now).Error);

    [Fact]
    public void Document_references_are_required() =>
        Assert.Equal(
            ConsentErrors.DocumentReferenceInvalid,
            ConsentPolicyVersion.CreateDraft(ConsentType.Referral, 1, " ", "en", Now).Error);

    [Fact]
    public void Undefined_consent_type_is_rejected() =>
        Assert.Equal(
            ConsentErrors.ConsentTypeInvalid,
            ConsentPolicyVersion.CreateDraft((ConsentType)9, 1, "ar", "en", Now).Error);

    [Fact]
    public void Approval_records_who_when_and_source()
    {
        var policy = Ok(ConsentPolicyVersion.CreateDraft(ConsentType.Participation, 1, "ar", "en", Now));

        Assert.True(policy.Approve(TestApproval(), Now).IsSuccess);

        Assert.Equal(ConsentPolicyStatus.Approved, policy.Status);
        Assert.Equal("approver-subject", policy.ApprovedBy);
        Assert.Equal(Now, policy.ApprovedAt);
        Assert.Equal("Synthetic test approval", policy.ApprovalSource);
    }

    [Fact]
    public void An_approved_policy_cannot_be_approved_again() =>
        Assert.Equal(ConsentErrors.PolicyNotDraft, ApprovedPolicy(ConsentType.Referral).Approve(TestApproval(), Now).Error);

    [Fact]
    public void Only_an_approved_policy_can_be_retired()
    {
        var draft = Ok(ConsentPolicyVersion.CreateDraft(ConsentType.Participation, 1, "ar", "en", Now));
        var approved = ApprovedPolicy(ConsentType.Participation);

        Assert.Equal(ConsentErrors.PolicyNotApproved, draft.Retire(Now).Error);
        Assert.True(approved.Retire(Now).IsSuccess);
        Assert.Equal(ConsentPolicyStatus.Retired, approved.Status);
        Assert.Equal(Now, approved.RetiredAt);
    }
}
