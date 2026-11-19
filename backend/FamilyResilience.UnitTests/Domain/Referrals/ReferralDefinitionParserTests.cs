using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.UnitTests.Domain.Referrals;

public sealed class ReferralDefinitionParserTests
{
    private const string Valid =
        """{"schema":"referral.v1","target":{"sectionCode":"A"},"routes":[{"categoryCode":"financial_support","when":{"tierCodeIn":["high_risk","moderate_risk"]}},{"categoryCode":"caregiver_support","when":{"tierCodeIn":["high_risk"]}}]}""";

    [Fact]
    public void A_valid_definition_reads_its_target_and_routes_in_order()
    {
        var definition = ReferralDefinitionParser.Parse(Valid).Value;

        Assert.Equal("A", definition.TargetSectionCode);
        Assert.Equal(["financial_support", "caregiver_support"], definition.Routes.Select(route => route.CategoryCode));
        Assert.Equal(["high_risk", "moderate_risk"], definition.Routes[0].TierCodes);
    }

    [Fact]
    public void A_missing_or_null_target_means_the_whole_version()
    {
        var withoutTarget = ReferralDefinitionParser.Parse(
            """{"schema":"referral.v1","routes":[{"categoryCode":"x_support","when":{"tierCodeIn":["low"]}}]}""");
        var nullTarget = ReferralDefinitionParser.Parse(
            """{"schema":"referral.v1","target":null,"routes":[{"categoryCode":"x_support","when":{"tierCodeIn":["low"]}}]}""");

        Assert.Null(withoutTarget.Value.TargetSectionCode);
        Assert.Null(nullTarget.Value.TargetSectionCode);
    }

    [Theory]
    [InlineData("", "$")]
    [InlineData("not json", "$")]
    [InlineData("[]", "$")]
    [InlineData("""{"schema":"referral.v2","routes":[]}""", "$.schema")]
    [InlineData("""{"schema":"referral.v1","routes":[],"extra":1}""", "$.extra")]
    [InlineData("""{"schema":"referral.v1","routes":[]}""", "$.routes")]
    [InlineData("""{"schema":"referral.v1","target":{"sectionCode":"a b"},"routes":[]}""", "$.target.sectionCode")]
    [InlineData("""{"schema":"referral.v1","routes":[{"categoryCode":"Bad Code","when":{"tierCodeIn":["low"]}}]}""", "$.routes[0].categoryCode")]
    [InlineData("""{"schema":"referral.v1","routes":[{"categoryCode":"x_support","when":{"tierCodeIn":[]}}]}""", "$.routes[0].when.tierCodeIn")]
    [InlineData("""{"schema":"referral.v1","routes":[{"categoryCode":"x_support","when":{"tierCodeIn":["low","low"]}}]}""", "$.routes[0].when.tierCodeIn")]
    [InlineData("""{"schema":"referral.v1","routes":[{"categoryCode":"x_support","when":{"scoreAbove":3}}]}""", "$.routes[0].when.scoreAbove")]
    [InlineData("""{"schema":"referral.v1","routes":[{"categoryCode":"x_support","when":{"tierCodeIn":["low"]}},{"categoryCode":"x_support","when":{"tierCodeIn":["high"]}}]}""", "$.routes[1].categoryCode")]
    public void An_invalid_definition_is_refused_at_its_path(string json, string path)
    {
        var error = ReferralDefinitionParser.Parse(json).Error;

        Assert.Equal(ReferralErrors.DefinitionInvalid.Code, error?.Code);
        Assert.Contains($" at {path}:", error!.Message, StringComparison.Ordinal);
    }
}
