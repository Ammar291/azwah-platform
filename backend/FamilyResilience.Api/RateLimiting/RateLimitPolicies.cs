namespace FamilyResilience.Api.RateLimiting;

/// <summary>
/// Named rate limits for sensitive operations, on top of the global limit every request passes. Each counts requests per
/// token subject (per remote address when anonymous) in a fixed window; the defaults below can be changed under
/// <c>RateLimiting:Policies:&lt;name&gt;:PermitLimit</c> and <c>:WindowSeconds</c>.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Household, member, roster, consent and session changes: 30 a minute.</summary>
    public const string ParticipantWrites = "participant-writes";

    /// <summary>Saving answers, which the app does after each choice: 120 a minute.</summary>
    public const string Answers = "answers";

    /// <summary>Sign-in and sign-out records: 10 a minute.</summary>
    public const string Identity = "identity";

    /// <summary>Aggregates, monitoring lists and the referral queue: 60 a minute, which also slows differencing attacks.</summary>
    public const string StaffReads = "staff-reads";

    /// <summary>Content, rules, partners, hand-offs and engine runs: 30 a minute.</summary>
    public const string StaffWrites = "staff-writes";

    /// <summary>Research export requests: 10 an hour (and the export quotas in the use case).</summary>
    public const string Exports = "exports";

    /// <summary>Research export downloads: 30 an hour.</summary>
    public const string ExportDownloads = "export-downloads";

    /// <summary>A family's copy of its data: 10 an hour.</summary>
    public const string DataExports = "data-exports";

    /// <summary>Support-resource directory searches: 30 a minute.</summary>
    public const string SupportSearches = "support-searches";

    /// <summary>Azwah assistant replies, each of which sends the family context to a model: 20 a minute.</summary>
    public const string Assistant = "assistant";

    /// <summary>Starts of a live Azwah consultant session: 6 a minute.</summary>
    public const string Consultant = "consultant";

    /// <summary>Written score reports, each a paid call that sends a session's answers to a model: 10 an hour.</summary>
    public const string ScoreReports = "score-reports";

    /// <summary>Report parts and summaries written as the person answers, several for each report: 120 an hour.</summary>
    public const string ScoreReportParts = "score-report-parts";

    public static IReadOnlyDictionary<string, RateLimitWindow> Defaults { get; } = new Dictionary<string, RateLimitWindow>(StringComparer.Ordinal)
    {
        [ParticipantWrites] = new(30, 60),
        [Answers] = new(120, 60),
        [Identity] = new(10, 60),
        [StaffReads] = new(60, 60),
        [StaffWrites] = new(30, 60),
        [Exports] = new(10, 3600),
        [ExportDownloads] = new(30, 3600),
        [DataExports] = new(10, 3600),
        [SupportSearches] = new(30, 60),
        [Assistant] = new(20, 60),
        [Consultant] = new(6, 60),
        [ScoreReports] = new(10, 3600),
        [ScoreReportParts] = new(120, 3600),
    };
}

/// <summary>At most <see cref="PermitLimit"/> requests in each window of <see cref="WindowSeconds"/>.</summary>
public sealed record RateLimitWindow(int PermitLimit, int WindowSeconds);
