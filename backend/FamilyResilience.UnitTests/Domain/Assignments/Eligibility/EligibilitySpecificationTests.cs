using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.UnitTests.Domain.Assignments.Eligibility;

public sealed class EligibilitySpecificationTests
{
    private static EligibilityFacts Facts(
        int age = 40,
        EmploymentStatus? employment = EmploymentStatus.Employed,
        bool head = false,
        bool earner = false,
        IReadOnlyList<int>? householdAges = null) =>
        new(FamilyMemberId.New(), true, age, age >= FamilyMember.AdultAge, employment, head, earner, householdAges ?? [age]);

    [Theory]
    [InlineData(59, Truth.False)]
    [InlineData(60, Truth.True)]
    public void Age_at_least_includes_its_bound(int age, Truth expected)
    {
        var result = new AgeAtLeast(60).Evaluate(Facts(age));

        Assert.Equal(expected, result.Truth);
        Assert.Equal(expected == Truth.False ? EligibilityReasons.AgeBelowMinimum : null, result.ReasonCode);
    }

    [Theory]
    [InlineData(55, Truth.True)]
    [InlineData(56, Truth.False)]
    public void Age_at_most_includes_its_bound(int age, Truth expected) =>
        Assert.Equal(expected, new AgeAtMost(55).Evaluate(Facts(age)).Truth);

    [Fact]
    public void Employment_status_in_is_unknown_when_the_status_is_missing()
    {
        var result = new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed }).Evaluate(Facts(employment: null));

        Assert.Equal(Truth.Unknown, result.Truth);
        Assert.Equal(new[] { MissingFacts.EmploymentStatus }, result.MissingInformation);
    }

    [Fact]
    public void Employment_status_in_names_the_reason_when_the_status_does_not_qualify()
    {
        var result = new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed })
            .Evaluate(Facts(employment: EmploymentStatus.Retired));

        Assert.Equal(Truth.False, result.Truth);
        Assert.Equal(EligibilityReasons.EmploymentStatusNotQualifying, result.ReasonCode);
    }

    [Fact]
    public void Head_and_primary_earner_rules_read_the_member_flags()
    {
        Assert.Equal(Truth.True, new IsHeadOfHousehold().Evaluate(Facts(head: true)).Truth);
        Assert.Equal(EligibilityReasons.NotHeadOfHousehold, new IsHeadOfHousehold().Evaluate(Facts()).ReasonCode);
        Assert.Equal(Truth.True, new IsPrimaryIncomeEarner().Evaluate(Facts(earner: true)).Truth);
        Assert.Equal(EligibilityReasons.NotPrimaryIncomeEarner, new IsPrimaryIncomeEarner().Evaluate(Facts()).ReasonCode);
    }

    [Theory]
    [InlineData(17, Truth.True)]
    [InlineData(18, Truth.False)]
    public void Household_has_member_younger_than_compares_strictly(int youngestAge, Truth expected) =>
        Assert.Equal(expected, new HouseholdHasMemberYoungerThan(18).Evaluate(Facts(householdAges: [40, youngestAge])).Truth);

    [Fact]
    public void All_of_is_decided_by_a_false_rule_even_when_another_lacks_information()
    {
        var rule = new AllOf([new AgeAtMost(55), new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed })]);

        var result = rule.Evaluate(Facts(age: 70, employment: null));

        Assert.Equal(Truth.False, result.Truth);
        Assert.Equal(EligibilityReasons.AgeAboveMaximum, result.ReasonCode);
        Assert.Empty(result.MissingInformation);
    }

    [Fact]
    public void All_of_is_unknown_when_no_rule_is_false_but_one_lacks_information()
    {
        var rule = new AllOf([new AgeAtMost(55), new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed })]);

        var result = rule.Evaluate(Facts(age: 30, employment: null));

        Assert.Equal(Truth.Unknown, result.Truth);
        Assert.Equal(new[] { MissingFacts.EmploymentStatus }, result.MissingInformation);
    }

    [Fact]
    public void All_of_reports_the_first_false_rule_in_order()
    {
        var result = new AllOf([new AgeAtLeast(25), new AgeAtMost(10)]).Evaluate(Facts(age: 20));

        Assert.Equal(EligibilityReasons.AgeBelowMinimum, result.ReasonCode);
    }

    [Fact]
    public void Any_of_is_decided_by_a_true_rule_even_when_another_lacks_information()
    {
        var rule = new AnyOf([new IsHeadOfHousehold(), new EmploymentStatusIn(new HashSet<EmploymentStatus> { EmploymentStatus.Employed })]);

        Assert.Equal(Truth.True, rule.Evaluate(Facts(employment: null, head: true)).Truth);
        Assert.Equal(Truth.Unknown, rule.Evaluate(Facts(employment: null)).Truth);
    }

    [Fact]
    public void Any_of_with_every_rule_false_says_no_alternative_was_met()
    {
        var result = new AnyOf([new IsHeadOfHousehold(), new IsPrimaryIncomeEarner()]).Evaluate(Facts());

        Assert.Equal(Truth.False, result.Truth);
        Assert.Equal(EligibilityReasons.NoneOfAlternativesMet, result.ReasonCode);
    }
}
