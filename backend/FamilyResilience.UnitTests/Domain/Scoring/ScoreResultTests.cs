using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class ScoreResultTests
{
    [Fact]
    public void Recording_keeps_the_rule_set_policy_contributions_and_categories_and_marks_the_session_scored()
    {
        var (session, version, _) = SubmittedSession();
        var rules = ApprovedRuleSet(RuleSetKind.Scoring, version);
        session.ClearDomainEvents();
        var computation = SyntheticComputation(session, "tier_2") with { CategoryScores = [new CategoryScore("finance", 2m, 2)] };

        var score = Ok(ScoreResult.Record(session, rules, computation, Now));

        Assert.Equal(session.Id, score.AssessmentSessionId);
        Assert.Equal(version.Id, score.InstrumentVersionId);
        Assert.Equal(rules.Id, score.RuleSetId);
        Assert.Equal("synthetic_policy", score.ScoringPolicyCode);
        Assert.Equal(1, score.ScoringAlgorithmVersion);
        Assert.Equal(ScoreStatus.Complete, score.Status);
        Assert.Null(score.IncompleteReason);
        Assert.Equal(4m, score.TotalScore);
        Assert.Equal("tier_2", score.TierCode);
        Assert.Equal(4, score.Contributions.Count);
        Assert.All(score.Contributions, contribution => Assert.Equal(score.Id, contribution.ScoreResultId));
        var category = Assert.Single(score.CategoryScores);
        Assert.Equal(("finance", 2m, 2), (category.CategoryCode, category.Score, category.ItemCount));
        Assert.Equal(SessionStatus.Scored, session.Status);
        Assert.Equal(Now, session.ScoredAt);
        Assert.IsType<ScoreResultRecorded>(Assert.Single(score.GetDomainEvents()));
    }

    [Fact]
    public void An_incomplete_result_has_no_total_tier_or_categories_and_keeps_its_contributions()
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session) with
        {
            Status = ScoreStatus.Incomplete,
            IncompleteReason = ScoreIncompleteReasons.NonSubstantiveShareExceeded,
            TotalScore = null,
        };

        var score = Ok(ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now));

        Assert.Equal(ScoreStatus.Incomplete, score.Status);
        Assert.Equal(ScoreIncompleteReasons.NonSubstantiveShareExceeded, score.IncompleteReason);
        Assert.Null(score.TotalScore);
        Assert.Equal(4, score.Contributions.Count);
    }

    [Fact]
    public void An_excluded_contribution_keeps_its_raw_value_and_has_no_calculated_value()
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session);
        var first = computation.Contributions[0] with { CalculatedValue = null };
        computation = computation with { Contributions = [first, .. computation.Contributions.Skip(1)] };

        var score = Ok(ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now));

        var excluded = score.Contributions.Single(contribution => contribution.InstrumentItemId == first.ItemId);
        Assert.True(excluded.IsExcluded);
        Assert.Null(excluded.CalculatedValue);
        Assert.Equal(first.RawValue, excluded.RawValue);
    }

    public static TheoryData<string> InconsistentComputations => ["incomplete_with_total", "complete_with_reason", "tier_without_total", "bad_policy"];

    [Theory]
    [MemberData(nameof(InconsistentComputations))]
    public void An_inconsistent_computation_is_refused(string kind)
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session);
        computation = kind switch
        {
            "incomplete_with_total" => computation with { Status = ScoreStatus.Incomplete, IncompleteReason = "no_substantive_answers" },
            "complete_with_reason" => computation with { IncompleteReason = "no_substantive_answers" },
            "tier_without_total" => computation with { TotalScore = null, TierCode = "tier_1" },
            _ => computation with { PolicyCode = "Not A Code" },
        };

        Assert.Equal(
            ScoringErrors.ComputationInconsistent,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now).Error);
        Assert.Equal(SessionStatus.Submitted, session.Status);
    }

    [Fact]
    public void Session_must_be_submitted()
    {
        var version = ApprovedVersion();
        var session = Ok(AssessmentSession.Start(Assign(NewHousehold().Members[0], version), Now));

        Assert.Equal(
            ScoringErrors.SessionNotSubmitted,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session), Now).Error);
    }

    [Fact]
    public void Rule_set_must_be_an_approved_scoring_rule_set_of_the_session_version()
    {
        var (session, version, _) = SubmittedSession();
        var proposed = Ok(RuleSet.Propose(RuleSetKind.Scoring, "rules", 1, version, "{}", Now));
        var eligibility = ApprovedRuleSet(RuleSetKind.Eligibility, version);
        var otherVersionRules = ApprovedRuleSet(RuleSetKind.Scoring, ApprovedVersion());
        var computation = SyntheticComputation(session);

        Assert.Equal(ScoringErrors.RuleSetNotApproved, ScoreResult.Record(session, proposed, computation, Now).Error);
        Assert.Equal(ScoringErrors.RuleSetWrongKind, ScoreResult.Record(session, eligibility, computation, Now).Error);
        Assert.Equal(ScoringErrors.RuleSetVersionMismatch, ScoreResult.Record(session, otherVersionRules, computation, Now).Error);
        Assert.Equal(SessionStatus.Submitted, session.Status);
    }

    [Fact]
    public void Contribution_raw_value_must_match_the_answer()
    {
        var (session, version, _) = SubmittedSession();
        var response = session.Responses[0];
        var computation = SyntheticComputation(session) with
        {
            Contributions = [new ItemContribution(response.InstrumentItemId, response.RawValue + 1, 1m, null)],
        };

        Assert.Equal(
            ScoringErrors.ContributionRawValueMismatch,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now).Error);
    }

    [Fact]
    public void Contribution_for_an_unanswered_item_is_rejected()
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session) with
        {
            Contributions = [new ItemContribution(InstrumentItemId.New(), 1, 1m, null)],
        };

        Assert.Equal(
            ScoringErrors.ContributionForUnansweredItem,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now).Error);
    }

    [Fact]
    public void Duplicate_contributions_are_rejected()
    {
        var (session, version, _) = SubmittedSession();
        var response = session.Responses[0];
        var contribution = new ItemContribution(response.InstrumentItemId, response.RawValue, 1m, null);

        Assert.Equal(
            ScoringErrors.DuplicateContribution,
            ScoreResult.Record(
                session, ApprovedRuleSet(RuleSetKind.Scoring, version),
                SyntheticComputation(session) with { Contributions = [contribution, contribution] }, Now).Error);
    }

    [Theory]
    [InlineData("1.2345")]
    [InlineData("1000000")]
    public void Score_that_does_not_fit_numeric_9_3_is_rejected(string total)
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session) with
        {
            TotalScore = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(
            ScoringErrors.ScoreOutOfRange,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now).Error);
    }

    [Fact]
    public void Tier_code_must_be_a_snake_code()
    {
        var (session, version, _) = SubmittedSession();

        Assert.Equal(
            ScoringErrors.TierCodeInvalid,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), SyntheticComputation(session, "Tier 1"), Now).Error);
    }

    [Fact]
    public void Category_codes_must_be_distinct()
    {
        var (session, version, _) = SubmittedSession();
        var computation = SyntheticComputation(session) with
        {
            CategoryScores = [new CategoryScore("finance", 1m, 1), new CategoryScore("finance", 2m, 1)],
        };

        Assert.Equal(
            ScoringErrors.ComputationInconsistent,
            ScoreResult.Record(session, ApprovedRuleSet(RuleSetKind.Scoring, version), computation, Now).Error);
    }

    [Fact]
    public void A_scored_session_can_be_scored_again_with_another_rule_set()
    {
        var (session, version, _) = ScoredSession();
        var later = Now.AddDays(1);

        var rescored = ScoreResult.Record(
            session, ApprovedRuleSet(RuleSetKind.Scoring, version, "test_rules_v2"), SyntheticComputation(session), later);

        Assert.True(rescored.IsSuccess);
        Assert.Equal(later, session.ScoredAt);
    }
}
