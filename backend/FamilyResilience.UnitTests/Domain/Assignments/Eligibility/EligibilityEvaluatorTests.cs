using System.Globalization;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments.Eligibility;

public sealed class EligibilityEvaluatorTests
{
    private static EligibilityRule Rule(IEligibilitySpecification specification) =>
        new(RuleSetId.New(), "test_rule", 3, "test_instrument", "A", null, specification);

    private static EligibilityFacts Facts(
        int age = 40, bool active = true, EmploymentStatus? employment = EmploymentStatus.Employed, bool? certainlyAdult = null) =>
        new(FamilyMemberId.New(), active, age, certainlyAdult ?? age >= FamilyMember.AdultAge, employment, false, false, [age]);

    [Fact]
    public void A_decision_records_the_rule_set_version_reason_and_time()
    {
        var rule = Rule(new AgeAtLeast(0));
        var facts = Facts();

        var decision = EligibilityEvaluator.Evaluate(rule, facts, Now);

        Assert.Equal(facts.MemberId, decision.MemberId);
        Assert.Equal("test_instrument", decision.InstrumentCode);
        Assert.Equal("A", decision.SectionCode);
        Assert.Equal(rule.RuleSetId, decision.RuleSetId);
        Assert.Equal("test_rule", decision.RuleCode);
        Assert.Equal(3, decision.RuleVersion);
        Assert.Equal(EligibilityOutcome.Eligible, decision.Outcome);
        Assert.True(decision.Eligible);
        Assert.Equal(EligibilityReasons.Eligible, decision.ReasonCode);
        Assert.Empty(decision.MissingInformation);
        Assert.Equal(Now, decision.EvaluatedAt);
    }

    [Fact]
    public void An_inactive_member_is_never_eligible()
    {
        var decision = EligibilityEvaluator.Evaluate(Rule(new AgeAtLeast(0)), Facts(active: false), Now);

        Assert.Equal(EligibilityOutcome.Ineligible, decision.Outcome);
        Assert.Equal(EligibilityReasons.MemberInactive, decision.ReasonCode);
    }

    [Fact]
    public void A_member_under_18_is_never_eligible_until_assent_rules_exist()
    {
        var decision = EligibilityEvaluator.Evaluate(Rule(new AgeAtLeast(0)), Facts(age: 17), Now);

        Assert.Equal(EligibilityOutcome.Ineligible, decision.Outcome);
        Assert.False(decision.Eligible);
        Assert.Equal(EligibilityReasons.MemberUnder18, decision.ReasonCode);
    }

    [Fact]
    public void A_member_who_is_18_by_the_convention_but_may_still_be_17_is_never_eligible()
    {
        var decision = EligibilityEvaluator.Evaluate(Rule(new AgeAtLeast(0)), Facts(age: 18, certainlyAdult: false), Now);

        Assert.Equal(EligibilityOutcome.Ineligible, decision.Outcome);
        Assert.Equal(EligibilityReasons.MemberUnder18, decision.ReasonCode);
    }

    // Born in November 2008 (say the 20th): 17 on 2 November 2026, certainly 18 only from 1 December 2026.
    [Theory]
    [InlineData("2026-11-02", EligibilityOutcome.Ineligible, EligibilityReasons.MemberUnder18)]
    [InlineData("2026-11-30", EligibilityOutcome.Ineligible, EligibilityReasons.MemberUnder18)]
    [InlineData("2026-12-01", EligibilityOutcome.Eligible, EligibilityReasons.Eligible)]
    public void The_adults_only_guard_waits_for_the_month_after_the_18th_birthday_month(
        string evaluatedOn, EligibilityOutcome outcome, string reasonCode)
    {
        var asOf = DateOnly.Parse(evaluatedOn, CultureInfo.InvariantCulture);
        var born = Ok(YearMonth.Create(2008, 11, asOf));
        var household = NewHousehold(Head(), new FamilyMemberDetails(born, HouseholdRelationship.Child, EmploymentStatus.Student, false, false));
        var facts = EligibilityFacts.From(household, household.Members[1], asOf);

        var decision = EligibilityEvaluator.Evaluate(Rule(new AgeAtLeast(0)), facts, Now);

        Assert.Equal(18, facts.Age);
        Assert.Equal((outcome, reasonCode), (decision.Outcome, decision.ReasonCode));
    }

    // The rule's own thresholds keep the documented convention (the birthday counts as the first of the birth month).
    [Fact]
    public void Rule_thresholds_still_count_the_birthday_as_the_first_of_the_birth_month()
    {
        var asOf = new DateOnly(2026, 11, 2);
        var born = Ok(YearMonth.Create(1966, 11, asOf));
        var household = NewHousehold(Head(), new FamilyMemberDetails(born, HouseholdRelationship.Parent, EmploymentStatus.Retired, false, false));

        var decision = EligibilityEvaluator.Evaluate(
            Rule(new AgeAtLeast(60)), EligibilityFacts.From(household, household.Members[1], asOf), Now);

        Assert.Equal(EligibilityOutcome.Eligible, decision.Outcome);
    }

    [Fact]
    public void An_indeterminate_decision_has_no_eligible_value_and_names_what_is_missing()
    {
        var rule = Rule(new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed }));

        var decision = EligibilityEvaluator.Evaluate(rule, Facts(employment: null), Now);

        Assert.Equal(EligibilityOutcome.Indeterminate, decision.Outcome);
        Assert.Null(decision.Eligible);
        Assert.Equal(EligibilityReasons.MissingInformation, decision.ReasonCode);
        Assert.Equal(new[] { MissingFacts.EmploymentStatus }, decision.MissingInformation);
    }

    [Fact]
    public void A_rule_set_binds_to_its_target_module()
    {
        var version = ApprovedVersion();
        var ruleSet = Ok(RuleSet.Propose(
            RuleSetKind.Eligibility, "module_b_rules", 2, version,
            """{"schema":"eligibility.v1","target":{"sectionCode":"B"},"rule":{"ageAtLeast":60}}""", Now));

        var rule = Ok(EligibilityRule.From(ruleSet, InstrumentOf(version), version));

        Assert.Equal("module_b_rules", rule.RuleCode);
        Assert.Equal(2, rule.RuleVersion);
        Assert.Equal("B", rule.SectionCode);
        Assert.Equal(version.Sections[1].Id, rule.SectionId);
        Assert.Equal(new AgeAtLeast(60), rule.Specification);
    }

    [Fact]
    public void A_rule_set_with_an_invalid_target_cannot_bind()
    {
        var version = ApprovedVersion();
        var ruleSet = Ok(RuleSet.Propose(
            RuleSetKind.Eligibility, "whole_version_rules", 1, version, """{"schema":"eligibility.v1","rule":{"ageAtLeast":60}}""", Now));

        Assert.Equal(QuestionnaireErrors.RuleSetTargetInvalid, EligibilityRule.From(ruleSet, InstrumentOf(version), version).Error);
    }
}
