namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Export limits from configuration section <c>Exports</c>: at most <c>MaxRows</c> rows per file (default 50 000), files
/// kept for <c>RetentionDays</c> (default 7, proposed until open question 12 sets retention), and a job that has not
/// moved for <c>StalledAfterHours</c> (default 24) failed by maintenance. <see cref="Bounded"/> keeps each in range.
/// </summary>
public sealed record ExportSettings(int MaxRows, int RetentionDays, int StalledAfterHours)
{
    public const int DefaultMaxRows = 50_000;
    public const int DefaultRetentionDays = 7;
    public const int DefaultStalledAfterHours = 24;

    public const int MaxMaxRows = 1_000_000;
    public const int MaxRetentionDays = 365;
    public const int MaxStalledAfterHours = 24 * 30;

    public static ExportSettings Default { get; } = new(DefaultMaxRows, DefaultRetentionDays, DefaultStalledAfterHours);

    public static ExportSettings Bounded(int maxRows, int retentionDays, int stalledAfterHours) =>
        new(
            Math.Clamp(maxRows, 1, MaxMaxRows),
            Math.Clamp(retentionDays, 1, MaxRetentionDays),
            Math.Clamp(stalledAfterHours, 1, MaxStalledAfterHours));
}
