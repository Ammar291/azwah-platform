using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments.Eligibility;

/// <summary>
/// The proposed module A, B and C rules exactly as committed in Infrastructure/Content. Proposed only: these tests
/// show what the rules would decide once approved; nothing here approves them.
/// </summary>
public sealed class ProposedFamilyVoicesRulesTests
{
    private static EligibilityRule Proposed(string fileName)
    {
        var definition = Ok(EligibilityDefinitionParser.Parse(ContentFiles.Read(fileName)));
        return new EligibilityRule(
            RuleSetId.New(), "proposed", 1, "abu_dhabi_family_survey", definition.TargetSectionCode, null, definition.Rule);
    }

    private static EligibilityDecision Decide(string fileName, Household household, FamilyMember member) =>
        EligibilityEvaluator.Evaluate(Proposed(fileName), EligibilityFacts.From(household, member, Today), Now);

    private static FamilyMemberDetails BornNextMonthOf(int yearsAgo, EmploymentStatus? employment = EmploymentStatus.Employed) =>
        // DomainTestData.Now is October, so next month is in the same year.
        new(Ok(YearMonth.Create(Today.Year - yearsAgo, Today.Month + 1, Today)), null, employment, false, false);

    [Fact]
    public void The_proposed_rules_target_modules_a_b_and_c() =>
        Assert.Equal(
            new[] { "A", "B", "C" },
            new[] { ContentFiles.FamilyVoicesModuleA, ContentFiles.FamilyVoicesModuleB, ContentFiles.FamilyVoicesModuleC }
                .Select(file => Proposed(file).SectionCode));

    [Theory]
    [InlineData(24, EligibilityOutcome.Ineligible, EligibilityReasons.AgeBelowMinimum)]
    [InlineData(25, EligibilityOutcome.Eligible, EligibilityReasons.Eligible)]
    [InlineData(55, EligibilityOutcome.Eligible, EligibilityReasons.Eligible)]
    [InlineData(56, EligibilityOutcome.Ineligible, EligibilityReasons.AgeAboveMaximum)]
    public void Module_b_admits_ages_25_to_55_inclusive(int age, EligibilityOutcome outcome, string reason)
    {
        var household = NewHousehold(Head(), Adult(age), Child(10));

        var decision = Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]);

        Assert.Equal(outcome, decision.Outcome);
        Assert.Equal(reason, decision.ReasonCode);
    }

    [Fact]
    public void Module_b_counts_age_in_completed_years_at_the_month_boundary()
    {
        var household = NewHousehold(Head(), BornNextMonthOf(25), BornNextMonthOf(56), Child(10));

        Assert.Equal(EligibilityReasons.AgeBelowMinimum, Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]).ReasonCode);
        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[2]).Outcome);
    }

    [Theory]
    [InlineData(EmploymentStatus.Employed, EligibilityOutcome.Eligible)]
    [InlineData(EmploymentStatus.SelfEmployed, EligibilityOutcome.Ineligible)]
    [InlineData(EmploymentStatus.Unemployed, EligibilityOutcome.Ineligible)]
    [InlineData(EmploymentStatus.Student, EligibilityOutcome.Ineligible)]
    [InlineData(EmploymentStatus.Homemaker, EligibilityOutcome.Ineligible)]
    [InlineData(EmploymentStatus.Retired, EligibilityOutcome.Ineligible)]
    [InlineData(EmploymentStatus.Other, EligibilityOutcome.Ineligible)]
    public void Module_b_counts_only_the_employed_status_as_employed(EmploymentStatus employment, EligibilityOutcome outcome)
    {
        var household = NewHousehold(Head(), Adult(35, employment: employment), Child(10));

        var decision = Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]);

        Assert.Equal(outcome, decision.Outcome);
        if (outcome == EligibilityOutcome.Ineligible)
        {
            Assert.Equal(EligibilityReasons.EmploymentStatusNotQualifying, decision.ReasonCode);
        }
    }

    [Fact]
    public void Module_b_is_indeterminate_when_employment_is_missing_and_everything_else_qualifies()
    {
        var household = NewHousehold(Head(), Adult(35, employment: null), Child(10));

        var decision = Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]);

        Assert.Equal(EligibilityOutcome.Indeterminate, decision.Outcome);
        Assert.Null(decision.Eligible);
        Assert.Equal(EligibilityReasons.MissingInformation, decision.ReasonCode);
        Assert.Equal(new[] { MissingFacts.EmploymentStatus }, decision.MissingInformation);
    }

    [Fact]
    public void Missing_employment_does_not_matter_when_the_age_already_excludes()
    {
        var household = NewHousehold(Head(), Adult(60, employment: null), Child(10));

        var decision = Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]);

        Assert.Equal(EligibilityOutcome.Ineligible, decision.Outcome);
        Assert.Equal(EligibilityReasons.AgeAboveMaximum, decision.ReasonCode);
        Assert.Empty(decision.MissingInformation);
    }

    [Fact]
    public void Module_b_needs_someone_under_18_in_the_household()
    {
        var household = NewHousehold(Head(), Adult(35));

        Assert.Equal(
            EligibilityReasons.NoHouseholdMemberUnderAge,
            Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]).ReasonCode);
    }

    [Theory]
    [InlineData(17, EligibilityOutcome.Eligible)]
    [InlineData(18, EligibilityOutcome.Ineligible)]
    public void Module_b_counts_only_members_younger_than_18(int youngestAge, EligibilityOutcome outcome)
    {
        var household = NewHousehold(Head(), Adult(35), Child(youngestAge));

        Assert.Equal(outcome, Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]).Outcome);
    }

    [Fact]
    public void An_inactive_child_does_not_count_for_module_b()
    {
        var household = NewHousehold(Head(), Adult(35), Child(10));
        Ok(household.DeactivateMember(household.Members[2].Id, Now));

        Assert.Equal(
            EligibilityReasons.NoHouseholdMemberUnderAge,
            Decide(ContentFiles.FamilyVoicesModuleB, household, household.Members[1]).ReasonCode);
    }

    [Theory]
    [InlineData(59, EligibilityOutcome.Ineligible)]
    [InlineData(60, EligibilityOutcome.Eligible)]
    public void Module_c_admits_members_aged_60_and_over(int age, EligibilityOutcome outcome)
    {
        var household = NewHousehold(Head(), Adult(age));

        Assert.Equal(outcome, Decide(ContentFiles.FamilyVoicesModuleC, household, household.Members[1]).Outcome);
    }

    [Fact]
    public void Module_a_admits_the_head_who_is_not_the_primary_earner()
    {
        var household = NewHousehold(Adult(45, head: true), Adult(40, earner: true, relationship: HouseholdRelationship.Spouse));

        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleA, household, household.Members[0]).Outcome);
        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleA, household, household.Members[1]).Outcome);
    }

    [Fact]
    public void Module_a_refuses_a_member_who_is_neither_head_nor_primary_earner()
    {
        var household = NewHousehold(Head(), Adult(30, relationship: HouseholdRelationship.Child));

        Assert.Equal(
            EligibilityReasons.NoneOfAlternativesMet,
            Decide(ContentFiles.FamilyVoicesModuleA, household, household.Members[1]).ReasonCode);
    }

    [Fact]
    public void A_member_can_qualify_for_several_modules_at_once()
    {
        var household = NewHousehold(Head(62), Child(10));
        var head = household.Members[0];

        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleA, household, head).Outcome);
        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleC, household, head).Outcome);
        Assert.Equal(EligibilityOutcome.Ineligible, Decide(ContentFiles.FamilyVoicesModuleB, household, head).Outcome);
    }

    [Fact]
    public void A_working_parent_who_heads_the_household_qualifies_for_a_and_b()
    {
        var household = NewHousehold(Head(40), Child(8));
        var head = household.Members[0];

        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleA, household, head).Outcome);
        Assert.Equal(EligibilityOutcome.Eligible, Decide(ContentFiles.FamilyVoicesModuleB, household, head).Outcome);
    }
}
