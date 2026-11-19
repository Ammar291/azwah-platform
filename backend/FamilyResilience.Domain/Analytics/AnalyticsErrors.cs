using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Analytics;

public static class AnalyticsErrors
{
    public static readonly Error ExportDatasetInvalid =
        new("analytics.export_dataset_invalid", "The dataset must be assessment_sessions or score_results.");

    public static readonly Error ExportColumnInvalid =
        new("analytics.export_column_invalid", "Columns must be 1 to 30 distinct column codes that the dataset allows.");

    public static readonly Error ExportFilterInvalid =
        new("analytics.export_filter_invalid", "An export filter names no wave, or is not an instrument code.");

    public static readonly Error ExportNotFound =
        new("analytics.export_not_found", "The export does not exist.");

    public static readonly Error ExportNotReady =
        new("analytics.export_not_ready", "The export has not completed.");

    public static readonly Error ExportExpired =
        new("analytics.export_expired", "The export has expired; request a new one.");

    public static readonly Error ExportStorageNotConfigured =
        new("analytics.export_storage_not_configured", "No export storage is configured in this environment.");

    public static readonly Error ExportTooLarge =
        new("analytics.export_too_large", "The export would exceed the maximum number of rows.");

    public static readonly Error ExportStalled =
        new("analytics.export_stalled", "The export did not finish in time; request a new one.");

    public static readonly Error ExportAttemptsExhausted =
        new("analytics.export_attempts_exhausted", "The export failed on its last attempt.");

    public static readonly Error ExportLimitReached =
        new("analytics.export_limit_reached", "Too many of your exports are still being written; wait for one to finish.");

    public static readonly Error ExportDailyLimitReached =
        new("analytics.export_daily_limit_reached", "You have requested the most exports allowed in 24 hours.");

    public static readonly Error ExportTransitionInvalid =
        new("analytics.export_transition_invalid", "The export cannot move to that state from its current one.");
}
