using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class ScoringContentTests
{
    [Fact]
    public void A_module_scope_holds_only_that_sections_items_in_order()
    {
        var version = ApprovedVersion();

        var content = Ok(ScoringContent.From(version, "B"));

        Assert.Equal(new[] { "B1", "B2" }, content.Items.Select(item => item.Code));
        Assert.Equal(new[] { 1, 2, 3 }, content.Items[0].Options.Select(option => option.Value));
        Assert.All(content.Items, item => Assert.Equal(ScoringDirection.Forward, item.Direction));
    }

    [Fact]
    public void A_whole_version_scope_holds_every_item() =>
        Assert.Equal(new[] { "A1", "A2", "B1", "B2" }, Ok(ScoringContent.From(ApprovedVersion(), null)).Items.Select(item => item.Code));

    [Fact]
    public void An_unknown_section_is_refused() =>
        Assert.Equal(QuestionnaireErrors.SectionNotFound, ScoringContent.From(ApprovedVersion(), "Z").Error);

    [Fact]
    public void Totals_range_over_the_substantive_values_of_scored_items()
    {
        var content = ScoringTestContent.Content(
            ScoringTestContent.Item("Q01", nonSubstantive: true),
            ScoringTestContent.Item("Q02", ScoringDirection.Reverse, scaleSize: 4),
            ScoringTestContent.Item("Q03", ScoringDirection.NotScored));

        Assert.Equal(2, content.MinimumTotal());
        Assert.Equal(9, content.MaximumTotal());
    }
}
