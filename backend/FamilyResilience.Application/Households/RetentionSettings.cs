using System.Globalization;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Retention periods from configuration section <c>Retention</c> (D-88). Off unless <c>Enabled</c> is <c>true</c>, and
/// each period has no default: an unset, zero, negative or unreadable period deletes nothing. The periods themselves
/// wait for approval (docs/retention-policy.md, open questions 12 and 22).
/// </summary>
public sealed record RetentionSettings(bool Enabled, int? WithdrawnHouseholdDays, int? ProcessedOutboxDays, int? FinishedExportJobsDays)
{
    public const string Section = "Retention";

    /// <summary>A hundred years: anything longer is a typo.</summary>
    private const int MaxDays = 36_500;

    public static RetentionSettings From(Func<string, string?> setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return new RetentionSettings(
            string.Equals(setting($"{Section}:Enabled"), "true", StringComparison.OrdinalIgnoreCase),
            Days(setting($"{Section}:WithdrawnHouseholdDays")),
            Days(setting($"{Section}:ProcessedOutboxDays")),
            Days(setting($"{Section}:FinishedExportJobsDays")));
    }

    private static int? Days(string? configured) =>
        int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 1 and <= MaxDays
            ? days
            : null;
}
