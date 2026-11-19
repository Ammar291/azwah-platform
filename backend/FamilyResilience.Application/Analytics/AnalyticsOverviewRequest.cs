namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Query string for the overview: an optional wave, the trend window (<c>from</c> and <c>to</c>, inclusive UTC dates,
/// default the year up to today) and the trend interval. Nothing else can be filtered or grouped.
/// </summary>
public sealed class AnalyticsOverviewRequest
{
    public const int MaxRangeDays = 3 * 366;

    public static readonly DateOnly EarliestDate = new(2000, 1, 1);

    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    public Guid? WaveId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public TrendInterval? Interval { get; init; }

    /// <summary>The window actually used: <c>to</c> defaults to today and <c>from</c> to a year before <c>to</c>.</summary>
    public (DateOnly From, DateOnly To) Window(DateOnly today)
    {
        var to = To ?? today;
        return (From ?? to.AddYears(-1), to);
    }
}
