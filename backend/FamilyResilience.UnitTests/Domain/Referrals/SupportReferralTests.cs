using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Referrals;

public sealed class SupportReferralTests
{
    private static (AssessmentSession Session, InstrumentVersion Version, FamilyMember Member) Scored()
    {
        var household = NewHousehold();
        var member = household.Members[0];
        var version = ApprovedVersion();
        var assignment = Assign(member, version);
        var session = Ok(AssessmentSession.Start(assignment, Now));
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Ok(session.Submit(version, assignment, Now));
        Ok(FamilyResilience.Domain.Scoring.ScoreResult.Record(
            session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session), Now));
        return (session, version, member);
    }

    [Fact]
    public void Referral_records_session_member_rules_and_consent()
    {
        var (session, version, member) = Scored();
        var rules = ApprovedRuleSet(RuleSetKind.Referral, version);
        var consent = ReferralConsentFor(member);

        var referral = Ok(SupportReferral.Create(session, rules, consent, "financial_counselling", Now));

        Assert.Equal(session.Id, referral.AssessmentSessionId);
        Assert.Equal(member.Id, referral.FamilyMemberId);
        Assert.Equal(version.Id, referral.InstrumentVersionId);
        Assert.Equal(rules.Id, referral.ReferralRuleSetId);
        Assert.Equal(consent.Id, referral.ConsentRecordId);
        Assert.Equal(ReferralStatus.Open, referral.Status);
        Assert.IsType<SupportReferralCreated>(Assert.Single(referral.GetDomainEvents()));
    }

    [Fact]
    public void Referral_needs_a_scored_session()
    {
        var (session, version, _) = SubmittedSession();

        Assert.Equal(
            ReferralErrors.SessionNotScored,
            SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(NewHousehold().Members[0]), "category", Now).Error);
    }

    [Fact]
    public void Referral_needs_an_approved_referral_rule_set_for_the_session_version()
    {
        var (session, version, member) = Scored();
        var consent = ReferralConsentFor(member);

        Assert.Equal(ReferralErrors.RuleSetWrongKind, SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Scoring, version), consent, "category", Now).Error);
        Assert.Equal(ReferralErrors.RuleSetNotApproved, SupportReferral.Create(session, Ok(RuleSet.Propose(RuleSetKind.Referral, "rules", 1, version, "{}", Now)), consent, "category", Now).Error);
        Assert.Equal(ReferralErrors.RuleSetVersionMismatch, SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, ApprovedVersion()), consent, "category", Now).Error);
    }

    [Fact]
    public void Referral_needs_active_referral_consent_from_the_session_member()
    {
        var (session, version, member) = Scored();
        var rules = ApprovedRuleSet(RuleSetKind.Referral, version);
        var withdrawn = ReferralConsentFor(member);
        Ok(withdrawn.Withdraw(null, Now));
        var participation = Ok(ConsentRecord.Grant(member, ApprovedPolicy(ConsentType.Participation), ConsentCaptureMethod.InApp, null, Now));
        var someoneElse = ReferralConsentFor(NewHousehold().Members[0]);

        Assert.Equal(ReferralErrors.ConsentNotActive, SupportReferral.Create(session, rules, withdrawn, "category", Now).Error);
        Assert.Equal(ReferralErrors.ConsentWrongType, SupportReferral.Create(session, rules, participation, "category", Now).Error);
        Assert.Equal(ReferralErrors.ConsentMemberMismatch, SupportReferral.Create(session, rules, someoneElse, "category", Now).Error);
    }

    [Fact]
    public void Category_must_be_a_snake_code()
    {
        var (session, version, member) = Scored();

        Assert.Equal(
            ReferralErrors.CategoryCodeInvalid,
            SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(member), "Debt Advice", Now).Error);
    }

    [Fact]
    public void Referral_moves_from_open_to_in_progress_to_resolved()
    {
        var (session, version, member) = Scored();
        var referral = Ok(SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(member), "category", Now));
        referral.ClearDomainEvents();

        Assert.True(referral.AssignService("family_support_service", Now.AddHours(1)).IsSuccess);
        Assert.Equal(ReferralStatus.InProgress, referral.Status);
        Assert.True(referral.Resolve("support_provided", "coordinator-subject", Now.AddDays(2)).IsSuccess);

        Assert.Equal(ReferralStatus.Resolved, referral.Status);
        Assert.Equal("family_support_service", referral.AssignedServiceCode);
        Assert.Equal("support_provided", referral.ResolutionCode);
        Assert.Equal("coordinator-subject", referral.ResolvedBy);
        Assert.Equal(Now.AddDays(2), referral.ResolvedAt);
        Assert.Equal(2, referral.GetDomainEvents().OfType<SupportReferralStatusChanged>().Count());
    }

    [Fact]
    public void Closed_referral_cannot_change()
    {
        var (session, version, member) = Scored();
        var referral = Ok(SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(member), "category", Now));
        Ok(referral.Cancel("consent_withdrawn", null, Now));

        Assert.Equal(ReferralErrors.AlreadyClosed, referral.Resolve("support_provided", null, Now).Error);
        Assert.Equal(ReferralErrors.NotOpen, referral.AssignService("service", Now).Error);
    }

    [Fact]
    public void Resolution_code_must_be_a_snake_code()
    {
        var (session, version, member) = Scored();
        var referral = Ok(SupportReferral.Create(session, ApprovedRuleSet(RuleSetKind.Referral, version), ReferralConsentFor(member), "category", Now));

        Assert.Equal(ReferralErrors.ResolutionCodeInvalid, referral.Resolve("Done!", null, Now).Error);
    }
}
