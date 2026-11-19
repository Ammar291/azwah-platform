namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Disclosure control for aggregates, from configuration section <c>Analytics</c>. The minimum group size of 10 was
/// approved by the product owner (D-58); 1 turns suppression off and is only for tests.
/// </summary>
public sealed record AnalyticsSettings(int MinimumGroupSize)
{
    public const int DefaultMinimumGroupSize = 10;

    public static AnalyticsSettings Default { get; } = new(DefaultMinimumGroupSize);
}
