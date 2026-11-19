namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Coarsened ages for aggregates and exports. An exact birth month or age never leaves the platform; only these bands do.
/// The birthday counts as the first of the birth month.
/// </summary>
public static class AgeBands
{
    /// <summary>The bands an adult falls in, in order (members under 18 are never assigned).</summary>
    public static IReadOnlyList<string> Adult { get; } = ["18_29", "30_44", "45_59", "60_plus"];

    public static string Of(int birthYear, int birthMonth, DateOnly asOf)
    {
        var age = asOf.Year - birthYear - (asOf.Month < birthMonth ? 1 : 0);
        return age switch
        {
            < 18 => "under_18",
            < 30 => "18_29",
            < 45 => "30_44",
            < 60 => "45_59",
            _ => "60_plus",
        };
    }
}
