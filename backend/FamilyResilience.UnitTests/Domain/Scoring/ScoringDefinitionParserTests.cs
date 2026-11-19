using System.Text.Json;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class ScoringDefinitionParserTests
{
    [Fact]
    public void A_definition_names_its_target_policy_version_and_parameters()
    {
        var parsed = ScoringDefinitionParser.Parse(
            """{"schema":"scoring.v1","target":{"sectionCode":"A"},"policy":"summed_item_values","algorithmVersion":1,"parameters":{"total":true}}""");

        Assert.True(parsed.IsSuccess, parsed.Error?.Message);
        Assert.Equal("A", parsed.Value.TargetSectionCode);
        Assert.Equal("summed_item_values", parsed.Value.PolicyCode);
        Assert.Equal(1, parsed.Value.AlgorithmVersion);
        Assert.Equal(JsonValueKind.Object, parsed.Value.Parameters.ValueKind);
    }

    [Fact]
    public void A_definition_without_a_target_covers_the_whole_version() =>
        Assert.Null(ScoringDefinitionParser.Parse(
            """{"schema":"scoring.v1","policy":"summed_item_values","algorithmVersion":1,"parameters":{}}""").Value.TargetSectionCode);

    [Theory]
    [InlineData("", "$")]
    [InlineData("[]", "$")]
    [InlineData("""{"schema":"scoring.v2","policy":"p","algorithmVersion":1,"parameters":{}}""", "$.schema")]
    [InlineData("""{"schema":"scoring.v1","policy":"Summed Values","algorithmVersion":1,"parameters":{}}""", "$.policy")]
    [InlineData("""{"schema":"scoring.v1","algorithmVersion":1,"parameters":{}}""", "$.policy")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":0,"parameters":{}}""", "$.algorithmVersion")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":"1","parameters":{}}""", "$.algorithmVersion")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":1.5,"parameters":{}}""", "$.algorithmVersion")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":1}""", "$.parameters")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":1,"parameters":[]}""", "$.parameters")]
    [InlineData("""{"schema":"scoring.v1","policy":"p","algorithmVersion":1,"parameters":{},"bands":[]}""", "$.bands")]
    [InlineData("""{"schema":"scoring.v1","target":{"sectionCode":"?"},"policy":"p","algorithmVersion":1,"parameters":{}}""", "$.target.sectionCode")]
    public void Invalid_definitions_are_refused_with_the_path_of_the_problem(string definition, string path)
    {
        var parsed = ScoringDefinitionParser.Parse(definition);

        Assert.Equal(ScoringErrors.DefinitionInvalid.Code, parsed.Error?.Code);
        Assert.Contains($" at {path}:", parsed.Error!.Message, StringComparison.Ordinal);
    }
}
