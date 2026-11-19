using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.UnitTests.Domain.Scoring;

public sealed class ScoringStrategyCatalogTests
{
    private static readonly ScoringStrategyCatalog Catalog = new([new SummedItemValuesStrategy()]);

    [Fact]
    public void A_known_policy_and_version_resolve_to_its_strategy() =>
        Assert.IsType<SummedItemValuesStrategy>(Catalog.Resolve("summed_item_values", 1).Value);

    [Theory]
    [InlineData("summed_item_values", 2)]
    [InlineData("weighted_sum", 1)]
    public void An_unknown_policy_or_algorithm_version_is_unsupported(string policy, int version) =>
        Assert.Equal(ScoringErrors.PolicyUnsupported, Catalog.Resolve(policy, version).Error);

    [Fact]
    public void A_policy_version_can_be_registered_once() =>
        Assert.Throws<ArgumentException>(() => new ScoringStrategyCatalog([new SummedItemValuesStrategy(), new SummedItemValuesStrategy()]));
}
