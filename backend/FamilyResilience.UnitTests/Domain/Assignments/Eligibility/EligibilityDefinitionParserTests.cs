using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.UnitTests.Domain.Assignments.Eligibility;

public sealed class EligibilityDefinitionParserTests
{
    [Fact]
    public void A_full_definition_parses_into_specifications_in_order()
    {
        var parsed = EligibilityDefinitionParser.Parse(
            """
            {
              "schema": "eligibility.v1",
              "target": { "sectionCode": "B" },
              "rule": { "all": [ { "ageAtLeast": 25 }, { "ageAtMost": 55 },
                                 { "employmentStatusIn": ["employed", "self_employed"] },
                                 { "householdHasMemberYoungerThan": 18 } ] }
            }
            """);

        Assert.True(parsed.IsSuccess, parsed.Error?.Message);
        Assert.Equal("B", parsed.Value.TargetSectionCode);
        var all = Assert.IsType<AllOf>(parsed.Value.Rule);
        Assert.Equal(new AgeAtLeast(25), all.Rules[0]);
        Assert.Equal(new AgeAtMost(55), all.Rules[1]);
        Assert.Equal(
            new[] { EmploymentStatus.Employed, EmploymentStatus.SelfEmployed },
            Assert.IsType<EmploymentStatusIn>(all.Rules[2]).Statuses.Order());
        Assert.Equal(new HouseholdHasMemberYoungerThan(18), all.Rules[3]);
    }

    [Theory]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60}}""")]
    [InlineData("""{"schema":"eligibility.v1","target":null,"rule":{"ageAtLeast":60}}""")]
    public void A_missing_or_null_target_means_the_whole_version(string definition) =>
        Assert.Null(EligibilityDefinitionParser.Parse(definition).Value.TargetSectionCode);

    [Fact]
    public void Head_or_primary_earner_parses_into_any_of() =>
        Assert.IsType<AnyOf>(EligibilityDefinitionParser.Parse(
            """{"schema":"eligibility.v1","target":{"sectionCode":"A"},"rule":{"any":[{"isHeadOfHousehold":true},{"isPrimaryIncomeEarner":true}]}}""").Value.Rule);

    [Theory]
    [InlineData("", "$")]
    [InlineData("not json", "$")]
    [InlineData("[]", "$")]
    [InlineData("""{"schema":"eligibility.v2","rule":{"ageAtLeast":60}}""", "$.schema")]
    [InlineData("""{"rule":{"ageAtLeast":60}}""", "$.schema")]
    [InlineData("""{"schema":"eligibility.v1"}""", "$.rule")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60},"extra":1}""", "$.extra")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60},"rule":{"ageAtLeast":61}}""", "$.rule")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60,"ageAtMost":70}}""", "$.rule")]
    [InlineData("""{"schema":"eligibility.v1","rule":{}}""", "$.rule")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":"60"}}""", "$.rule.ageAtLeast")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":60.5}}""", "$.rule.ageAtLeast")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtLeast":-1}}""", "$.rule.ageAtLeast")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"ageAtMost":131}}""", "$.rule.ageAtMost")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"householdHasMemberYoungerThan":0}}""", "$.rule.householdHasMemberYoungerThan")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"all":[]}}""", "$.rule.all")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"any":{}}}""", "$.rule.any")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"all":[{"ageAtLeast":1},{"isAdult":true}]}}""", "$.rule.all[1].isAdult")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"employmentStatusIn":[]}}""", "$.rule.employmentStatusIn")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"employmentStatusIn":["working"]}}""", "$.rule.employmentStatusIn[0]")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"employmentStatusIn":["Employed"]}}""", "$.rule.employmentStatusIn[0]")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"employmentStatusIn":["employed","employed"]}}""", "$.rule.employmentStatusIn[1]")]
    [InlineData("""{"schema":"eligibility.v1","rule":{"isHeadOfHousehold":false}}""", "$.rule.isHeadOfHousehold")]
    [InlineData("""{"schema":"eligibility.v1","target":{"sectionCode":""},"rule":{"ageAtLeast":60}}""", "$.target.sectionCode")]
    [InlineData("""{"schema":"eligibility.v1","target":{"section":"A"},"rule":{"ageAtLeast":60}}""", "$.target.section")]
    public void Invalid_definitions_are_refused_with_the_path_of_the_problem(string definition, string path)
    {
        var parsed = EligibilityDefinitionParser.Parse(definition);

        Assert.Equal(EligibilityErrors.DefinitionInvalid.Code, parsed.Error?.Code);
        Assert.Contains($" at {path}:", parsed.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_may_not_nest_deeper_than_eight_levels()
    {
        var rule = """{"ageAtLeast":1}""";
        for (var level = 0; level < EligibilityDefinitionParser.MaxDepth; level++)
        {
            rule = $$"""{"all":[{{rule}}]}""";
        }

        var parsed = EligibilityDefinitionParser.Parse($$"""{"schema":"eligibility.v1","rule":{{rule}}}""");

        Assert.Equal(EligibilityErrors.DefinitionInvalid.Code, parsed.Error?.Code);
    }
}
