namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>The metric definitions the evaluation reports rest on. Pure arithmetic; no host or database.</summary>
public sealed class RetrievalMetricsTests
{
    private static readonly HashSet<string> Relevant = new(StringComparer.Ordinal) { "a", "b" };

    [Fact]
    public void Precision_counts_relevant_results_among_the_first_k_over_k() =>
        Assert.Equal(0.4, RetrievalMetrics.PrecisionAtK(["x", "a", "y", "b", "z", "w"], Relevant, 5), 10);

    [Fact]
    public void Precision_divides_by_k_even_when_fewer_results_came_back() =>
        Assert.Equal(0.2, RetrievalMetrics.PrecisionAtK(["a"], Relevant, 5), 10);

    [Fact]
    public void Recall_counts_relevant_results_among_the_first_k_over_all_relevant() =>
        Assert.Equal(0.5, RetrievalMetrics.RecallAtK(["x", "y", "z", "w", "a", "b"], Relevant, 5), 10);

    [Fact]
    public void Recall_is_undefined_without_relevant_results() =>
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.RecallAtK(["a"], new HashSet<string>(), 5));

    [Theory]
    [InlineData(new[] { "a", "x" }, 1.0)]
    [InlineData(new[] { "x", "y", "b" }, 1.0 / 3)]
    [InlineData(new[] { "x", "y" }, 0.0)]
    [InlineData(new string[0], 0.0)]
    public void Reciprocal_rank_is_one_over_the_rank_of_the_first_relevant_result(string[] returned, double expected) =>
        Assert.Equal(expected, RetrievalMetrics.ReciprocalRank(returned, Relevant), 10);

    [Theory]
    [InlineData(50, 30.0)]
    [InlineData(95, 50.0)]
    [InlineData(20, 10.0)]
    [InlineData(21, 20.0)]
    [InlineData(100, 50.0)]
    [InlineData(0, 10.0)]
    public void Percentile_is_the_nearest_rank_value(double p, double expected) =>
        Assert.Equal(expected, RetrievalMetrics.Percentile([50.0, 10.0, 40.0, 20.0, 30.0], p));

    [Fact]
    public void Percentile_is_undefined_for_no_values() =>
        Assert.Throws<ArgumentException>(() => RetrievalMetrics.Percentile([], 50));
}
