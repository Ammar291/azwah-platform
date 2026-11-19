using System.Globalization;
using System.Text.Json;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// The only columns a research export can contain, per dataset, and how each is produced. Allowlist and projection are
/// one table, so a column cannot be offered without a defined, de-identified value. No column carries an ID, a
/// reference, a name, an exact date or a birth month: people appear as per-export pseudonyms, ages as bands and dates
/// as months. No column carries an answer to an item either: <c>non_substantive_count</c> and <c>excluded_count</c>
/// say only how many answers were "prefer not to answer" (99), so a researcher can tell a prorated total from a full
/// one and filter by the manual's 30% rule, and <c>incomplete_reason</c> is a fixed code of the scoring policy.
/// </summary>
public static class ExportCatalog
{
    private const string NotRecorded = "not_recorded";

    private static readonly (string Code, ExportDataset Dataset)[] Datasets =
    [
        ("assessment_sessions", ExportDataset.AssessmentSessions),
        ("score_results", ExportDataset.ScoreResults),
    ];

    private static readonly (string Column, Func<ExportSessionRow, Pseudonymizer, string?> Value)[] SessionColumns =
    [
        ("participant_pseudonym", (row, pseudonyms) => pseudonyms.Member(row.MemberId)),
        ("household_pseudonym", (row, pseudonyms) => pseudonyms.Household(row.HouseholdId)),
        ("instrument_code", (row, _) => row.InstrumentCode),
        ("version_number", (row, _) => Number(row.VersionNumber)),
        ("section_code", (row, _) => row.SectionCode),
        ("wave_code", (row, _) => row.WaveCode),
        ("session_status", (row, _) => Snake(row.Status)),
        ("submitted_month", (row, _) => Month(row.SubmittedAt)),
        ("age_band", (row, _) => AgeBands.Of(row.BirthYear, row.BirthMonth, Day(row.SubmittedAt))),
        ("employment_status", (row, _) => row.EmploymentStatus is { } status ? Snake(status) : NotRecorded),
        ("is_head_of_household", (row, _) => row.IsHeadOfHousehold ? "true" : "false"),
        ("answered_count", (row, _) => Number(row.AnsweredCount)),
        ("non_substantive_count", (row, _) => Number(row.NonSubstantiveCount)),
    ];

    private static readonly (string Column, Func<ExportScoreRow, Pseudonymizer, string?> Value)[] ScoreColumns =
    [
        ("participant_pseudonym", (row, pseudonyms) => pseudonyms.Member(row.MemberId)),
        ("household_pseudonym", (row, pseudonyms) => pseudonyms.Household(row.HouseholdId)),
        ("instrument_code", (row, _) => row.InstrumentCode),
        ("version_number", (row, _) => Number(row.VersionNumber)),
        ("section_code", (row, _) => row.SectionCode),
        ("wave_code", (row, _) => row.WaveCode),
        ("rule_set_code", (row, _) => row.RuleSetCode),
        ("rule_set_version", (row, _) => Number(row.RuleSetVersion)),
        ("score_status", (row, _) => Snake(row.Status)),
        ("incomplete_reason", (row, _) => row.IncompleteReason),
        ("total_score", (row, _) => row.TotalScore is { } total ? Score(total) : null),
        ("tier_code", (row, _) => row.TierCode),
        ("excluded_count", (row, _) => Number(row.ExcludedCount)),
        ("scored_month", (row, _) => Month(row.ComputedAt)),
        ("age_band", (row, _) => AgeBands.Of(row.BirthYear, row.BirthMonth, Day(row.ComputedAt))),
        ("employment_status", (row, _) => row.EmploymentStatus is { } status ? Snake(status) : NotRecorded),
    ];

    /// <summary>Every dataset code, in catalog order.</summary>
    public static IReadOnlyList<string> DatasetCodes { get; } = [.. Datasets.Select(dataset => dataset.Code)];

    /// <summary>The filters an export request may set, by their snake-case names.</summary>
    public static IReadOnlyList<string> FilterCodes { get; } = ["wave_id", "instrument_code"];

    public static Result<ExportDataset> ParseDataset(string? code)
    {
        foreach (var (known, dataset) in Datasets)
        {
            if (string.Equals(code, known, StringComparison.Ordinal))
            {
                return Result.Success(dataset);
            }
        }

        return Result.Failure<ExportDataset>(AnalyticsErrors.ExportDatasetInvalid);
    }

    public static string CodeOf(ExportDataset dataset) =>
        Datasets.Single(entry => entry.Dataset == dataset).Code;

    public static IReadOnlyList<string> ColumnsOf(ExportDataset dataset) => dataset switch
    {
        ExportDataset.AssessmentSessions => [.. SessionColumns.Select(column => column.Column)],
        ExportDataset.ScoreResults => [.. ScoreColumns.Select(column => column.Column)],
        _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset, "Unknown export dataset."),
    };

    /// <summary>Whether every requested column is in the dataset's allowlist.</summary>
    public static Result CheckColumns(ExportDataset dataset, IReadOnlyList<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var allowed = ColumnsOf(dataset);
        return columns.All(column => allowed.Contains(column, StringComparer.Ordinal))
            ? Result.Success()
            : Result.Failure(AnalyticsErrors.ExportColumnInvalid);
    }

    public static IReadOnlyList<string?> Project(ExportSessionRow row, IReadOnlyList<string> columns, Pseudonymizer pseudonyms) =>
        Project(SessionColumns, row, columns, pseudonyms);

    public static IReadOnlyList<string?> Project(ExportScoreRow row, IReadOnlyList<string> columns, Pseudonymizer pseudonyms) =>
        Project(ScoreColumns, row, columns, pseudonyms);

    private static IReadOnlyList<string?> Project<TRow>(
        (string Column, Func<TRow, Pseudonymizer, string?> Value)[] table, TRow row, IReadOnlyList<string> columns, Pseudonymizer pseudonyms)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(pseudonyms);
        return columns.Select(column => table.Single(entry => entry.Column == column).Value(row, pseudonyms)).ToList();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The column is numeric(9,3), so a stored 107 arrives as 107.000: written without trailing zeros.</summary>
    private static string Score(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Month(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateOnly Day(DateTimeOffset time) => DateOnly.FromDateTime(time.UtcDateTime);

    private static string Snake<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
