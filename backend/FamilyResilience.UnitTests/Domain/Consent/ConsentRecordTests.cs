using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Consent;

public sealed class ConsentRecordTests
{
    [Fact]
    public void Grant_copies_member_household_and_policy_type_and_raises_consent_granted()
    {
        var household = NewHousehold();
        var member = household.Members[0];
        var policy = ApprovedPolicy(ConsentType.Referral);

        var consent = Ok(ConsentRecord.Grant(member, policy, ConsentCaptureMethod.InApp, "subject-1", Now));

        Assert.Equal(member.Id, consent.FamilyMemberId);
        Assert.Equal(household.Id, consent.HouseholdId);
        Assert.Equal(ConsentType.Referral, consent.ConsentType);
        Assert.Equal(policy.Id, consent.ConsentPolicyVersionId);
        Assert.Equal(Now, consent.GrantedAt);
        Assert.Equal("subject-1", consent.GrantedBy);
        Assert.True(consent.IsActive);
        var raised = Assert.IsType<ConsentGranted>(Assert.Single(consent.GetDomainEvents()));
        Assert.Equal(ConsentType.Referral, raised.ConsentType);
    }

    [Fact]
    public void Consent_cannot_be_granted_against_a_draft_policy()
    {
        var draft = Ok(ConsentPolicyVersion.CreateDraft(ConsentType.Referral, 1, "ar", "en", Now));

        Assert.Equal(
            ConsentErrors.PolicyNotApproved,
            ConsentRecord.Grant(NewHousehold().Members[0], draft, ConsentCaptureMethod.InApp, null, Now).Error);
    }

    [Fact]
    public void Consent_cannot_be_granted_for_an_inactive_member()
    {
        var household = NewHousehold(Head(), Child(9));
        var child = household.Members[1];
        Ok(household.DeactivateMember(child.Id, Now));

        Assert.Equal(
            ConsentErrors.MemberNotActive,
            ConsentRecord.Grant(child, ApprovedPolicy(ConsentType.Participation), ConsentCaptureMethod.PaperForm, null, Now).Error);
    }

    [Fact]
    public void Undefined_capture_method_is_rejected() =>
        Assert.Equal(
            ConsentErrors.CaptureMethodInvalid,
            ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.Referral), (ConsentCaptureMethod)0, null, Now).Error);

    [Fact]
    public void Granted_by_longer_than_200_characters_is_rejected() =>
        Assert.Equal(
            ConsentErrors.ActorIdTooLong,
            ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.Referral), ConsentCaptureMethod.InApp, new string('s', 201), Now).Error);

    [Fact]
    public void Withdrawal_is_a_state_change_that_raises_consent_withdrawn()
    {
        var consent = Ok(ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.ResearchUse), ConsentCaptureMethod.InApp, null, Now));
        consent.ClearDomainEvents();
        var later = Now.AddDays(3);

        Assert.True(consent.Withdraw("subject-2", later).IsSuccess);

        Assert.False(consent.IsActive);
        Assert.Equal(later, consent.WithdrawnAt);
        Assert.Equal("subject-2", consent.WithdrawnBy);
        Assert.IsType<ConsentWithdrawn>(Assert.Single(consent.GetDomainEvents()));
    }

    [Fact]
    public void Withdrawn_consent_cannot_be_withdrawn_again()
    {
        var consent = Ok(ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.ResearchUse), ConsentCaptureMethod.InApp, null, Now));
        Ok(consent.Withdraw(null, Now));

        Assert.Equal(ConsentErrors.NotActive, consent.Withdraw(null, Now).Error);
    }

    [Fact]
    public void Withdrawal_before_the_grant_is_rejected()
    {
        var consent = Ok(ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.ResearchUse), ConsentCaptureMethod.InApp, null, Now));

        Assert.Equal(ConsentErrors.WithdrawnBeforeGranted, consent.Withdraw(null, Now.AddMinutes(-1)).Error);
    }

    [Fact]
    public void Withdrawal_time_must_be_utc()
    {
        var consent = Ok(ConsentRecord.Grant(NewHousehold().Members[0], ApprovedPolicy(ConsentType.ResearchUse), ConsentCaptureMethod.InApp, null, Now));

        Assert.Equal(CommonErrors.TimeNotUtc, consent.Withdraw(null, Now.ToOffset(TimeSpan.FromHours(4))).Error);
    }
}
