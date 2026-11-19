using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Referrals;

public sealed class SupportRecommendationTests
{
    private static ReferralDefinition Routes(params (string Category, string[] Tiers)[] routes) =>
        new(null, routes.Select(route => new ReferralRoute(route.Category, route.Tiers)).ToList());

    /// <summary>A whole-version session of a known member, every item answered with its first option, scored with tier test_tier.</summary>
    private static (AssessmentSession Session, InstrumentVersion Version, FamilyMember Member, ScoreResult Score) Scored()
    {
        var member = NewHousehold().Members[0];
        var version = ApprovedVersion();
        var assignment = Assign(member, version);
        var session = Ok(AssessmentSession.Start(assignment, Now));
        foreach (var item in version.GetItems())
        {
            Ok(session.Answer(version, item.Id, item.Options[0].Id, Now));
        }

        Ok(session.Submit(version, assignment, Now));
        var score = Ok(ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session, "test_tier"), Now));
        return (session, version, member, score);
    }

    [Fact]
    public void A_complete_score_routes_to_every_category_whose_tiers_include_it()
    {
        var (_, _, _, score) = Scored();
        var definition = Routes(("first_support", ["test_tier", "other"]), ("second_support", ["other"]), ("third_support", ["test_tier"]));

        Assert.Equal(["first_support", "third_support"], definition.CategoriesFor(score));
    }

    [Fact]
    public void An_untiered_or_incomplete_score_routes_nowhere()
    {
        var (session, version, _) = SubmittedSession();
        var untiered = Ok(ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session), Now));
        var (incompleteSession, incompleteVersion, _) = SubmittedSession();
        var incomplete = Ok(ScoreResult.Record(
            incompleteSession,
            ApprovedRuleSet(RuleSetKind.Scoring, incompleteVersion),
            SyntheticComputation(incompleteSession) with
            {
                Status = ScoreStatus.Incomplete, IncompleteReason = "too_many_declined", TotalScore = null, TierCode = null,
            },
            Now));
        var definition = Routes(("any_support", ["test_tier"]));

        Assert.Empty(definition.CategoriesFor(untiered));
        Assert.Empty(definition.CategoriesFor(incomplete));
    }

    [Fact]
    public void A_recommendation_records_its_score_rules_and_referral()
    {
        var (session, version, member, score) = Scored();
        var rules = ApprovedRuleSet(RuleSetKind.Referral, version);
        var referral = Ok(SupportReferral.Create(session, rules, ReferralConsentFor(member), "first_support", Now));

        var recommendation = Ok(SupportRecommendation.Record(session, score, rules, "first_support", referral, Now));

        Assert.Equal(
            (session.Id, member.Id, version.Id, score.Id, rules.Id, "first_support", (SupportReferralId?)referral.Id),
            (recommendation.AssessmentSessionId, recommendation.FamilyMemberId, recommendation.InstrumentVersionId,
                recommendation.ScoreResultId, recommendation.ReferralRuleSetId, recommendation.CategoryCode, recommendation.SupportReferralId));
    }

    [Fact]
    public void Without_a_referral_the_recommendation_stands_alone()
    {
        var (session, version, _, score) = Scored();

        var recommendation = Ok(SupportRecommendation.Record(session, score, ApprovedRuleSet(RuleSetKind.Referral, version), "first_support", null, Now));

        Assert.Null(recommendation.SupportReferralId);
    }

    [Fact]
    public void A_recommendation_needs_an_approved_referral_rule_set_of_the_sessions_version()
    {
        var (session, version, _, score) = Scored();

        Assert.Equal(
            ReferralErrors.RuleSetWrongKind,
            SupportRecommendation.Record(session, score, ApprovedRuleSet(RuleSetKind.Scoring, version, "scoring_again"), "first_support", null, Now).Error);
        Assert.Equal(
            ReferralErrors.RuleSetVersionMismatch,
            SupportRecommendation.Record(session, score, ApprovedRuleSet(RuleSetKind.Referral, ApprovedVersion()), "first_support", null, Now).Error);
    }

    [Fact]
    public void A_recommendation_needs_a_scored_session_its_own_score_and_a_snake_case_category()
    {
        var (session, version, _, score) = Scored();
        var (_, _, _, otherScore) = Scored();
        var (submitted, submittedVersion, _) = SubmittedSession();
        var rules = ApprovedRuleSet(RuleSetKind.Referral, version);

        Assert.Equal(ReferralErrors.ScoreMismatch, SupportRecommendation.Record(session, otherScore, rules, "first_support", null, Now).Error);
        Assert.Equal(ReferralErrors.CategoryCodeInvalid, SupportRecommendation.Record(session, score, rules, "Not A Code", null, Now).Error);
        Assert.Equal(
            CommonErrors.TimeNotUtc,
            SupportRecommendation.Record(session, score, rules, "first_support", null, Now.ToOffset(TimeSpan.FromHours(4))).Error);
        Assert.Equal(
            ReferralErrors.SessionNotScored,
            SupportRecommendation.Record(submitted, score, ApprovedRuleSet(RuleSetKind.Referral, submittedVersion), "first_support", null, Now).Error);
    }

    [Fact]
    public void A_recommendation_refuses_a_referral_for_another_category()
    {
        var (session, version, member, score) = Scored();
        var rules = ApprovedRuleSet(RuleSetKind.Referral, version);
        var referral = Ok(SupportReferral.Create(session, rules, ReferralConsentFor(member), "first_support", Now));

        Assert.Equal(ReferralErrors.ReferralMismatch, SupportRecommendation.Record(session, score, rules, "second_support", referral, Now).Error);
    }
}
