using System.Text.Json;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// The analytics overview: enrolment, assignment, start and completion counts, completion by version and over time, and
/// completion by age band and employment status. The database counts; this use case only rolls counts up, computes
/// rates and suppresses demographic cells that cover fewer people than the minimum, whatever number of assignments
/// those people hold (D-58). Every read is audited. Scores are not part of it (D-62).
/// </summary>
public sealed class GetAnalyticsOverview(IAnalyticsQueries queries, AnalyticsSettings settings, IAuditLog auditLog, TimeProvider timeProvider)
{
    private const string NotRecorded = "not_recorded";

    public async Task<Result<AnalyticsOverviewResponse>> HandleAsync(AnalyticsOverviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var (from, to) = request.Window(today);
        var interval = request.Interval ?? TrendInterval.Month;
        var wave = request.WaveId is { } waveId ? WaveId.From(waveId) : (WaveId?)null;

        var enrollment = await queries.EnrollmentAsync(cancellationToken);
        var statuses = await queries.AssignmentStatusesAsync(wave, cancellationToken);
        var started = await queries.StartedAssignmentsAsync(wave, cancellationToken);
        var byVersion = await queries.AssignmentStatusesByVersionAsync(wave, cancellationToken);
        var days = await queries.CompletionsByDayAsync(
            wave, Start(from), Start(to.AddDays(1)), cancellationToken);
        var births = await queries.AssignmentsByBirthMonthAsync(wave, cancellationToken);
        var employment = await queries.AssignmentsByEmploymentAsync(wave, cancellationToken);

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("analytics.overview.read", "analytics_overview", null, AuditOutcome.Succeeded), cancellationToken);
        if (recorded.IsFailure)
        {
            return Result.Failure<AnalyticsOverviewResponse>(recorded.Error!);
        }

        var minimum = settings.MinimumGroupSize;
        return Result.Success(new AnalyticsOverviewResponse(
            now,
            minimum,
            request.WaveId,
            from,
            to,
            interval,
            enrollment,
            Totals(statuses, started),
            Versions(byVersion),
            Trend(days, from, to, interval),
            new DemographicsResponse(
                today,
                Breakdown(
                    births.Select(row => new GroupCounts(
                        AgeBands.Of(row.BirthYear, row.BirthMonth, today), row.Assignments, row.Completed, row.Members, row.MembersCompleted)),
                    AgeBands.Adult,
                    minimum),
                Breakdown(
                    employment.Select(row => new GroupCounts(
                        EmploymentGroup(row.EmploymentStatus), row.Assignments, row.Completed, row.Members, row.MembersCompleted)),
                    EmploymentGroups,
                    minimum))));
    }

    private static readonly string[] EmploymentGroups =
        [.. Enum.GetValues<EmploymentStatus>().Select(status => EmploymentGroup(status)), NotRecorded];

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static decimal? Rate(int completed, int total) =>
        total == 0 ? null : Math.Round((decimal)completed / total, 4, MidpointRounding.AwayFromZero);

    private static AssignmentTotalsResponse Totals(IReadOnlyList<StatusCount> statuses, int started)
    {
        int Count(AssignmentStatus status) => statuses.Where(row => row.Status == status).Sum(row => row.Count);
        var total = statuses.Sum(row => row.Count);
        var completed = Count(AssignmentStatus.Completed);
        return new AssignmentTotalsResponse(
            total,
            started,
            Count(AssignmentStatus.Assigned),
            Count(AssignmentStatus.InProgress),
            completed,
            Count(AssignmentStatus.Expired),
            Count(AssignmentStatus.Cancelled),
            Rate(completed, total - Count(AssignmentStatus.Cancelled)));
    }

    private static IReadOnlyList<VersionCompletionResponse> Versions(IReadOnlyList<VersionStatusCount> rows) =>
        rows.GroupBy(row => (row.InstrumentCode, row.VersionNumber, row.SectionCode))
            .OrderBy(group => group.Key.InstrumentCode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.VersionNumber)
            .ThenBy(group => group.Key.SectionCode, StringComparer.Ordinal)
            .Select(group =>
            {
                var total = group.Where(row => row.Status != AssignmentStatus.Cancelled).Sum(row => row.Count);
                var completed = group.Where(row => row.Status == AssignmentStatus.Completed).Sum(row => row.Count);
                return new VersionCompletionResponse(
                    group.Key.InstrumentCode, group.Key.VersionNumber, group.Key.SectionCode, total, completed, Rate(completed, total));
            })
            .ToList();

    private static IReadOnlyList<TrendPointResponse> Trend(IReadOnlyList<DayCount> days, DateOnly from, DateOnly to, TrendInterval interval)
    {
        var points = new List<TrendPointResponse>();
        for (var start = PeriodStart(from, interval); start <= to; start = interval == TrendInterval.Month ? start.AddMonths(1) : start.AddDays(7))
        {
            var period = start;
            points.Add(new TrendPointResponse(
                period, days.Where(day => PeriodStart(day.Day, interval) == period).Sum(day => day.Count)));
        }

        return points;
    }

    /// <summary>The first day of the month, or the Monday of the ISO week, that <paramref name="day"/> falls in.</summary>
    private static DateOnly PeriodStart(DateOnly day, TrendInterval interval) =>
        interval == TrendInterval.Month
            ? new DateOnly(day.Year, day.Month, 1)
            : day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// One row per group, in <paramref name="order"/>, with totals and completions suppressed column by column on the
    /// distinct members behind each cell. Rows of one group add up: a member has one birth month and one employment
    /// status, so no member is counted in two rows.
    /// </summary>
    private static IReadOnlyList<DemographicRowResponse> Breakdown(IEnumerable<GroupCounts> rows, IReadOnlyList<string> order, int minimum)
    {
        var list = rows.ToList();
        var groups = order.Concat(list.Select(row => row.Group).Where(group => !order.Contains(group)).Distinct()).ToList();
        List<int> Sum(Func<GroupCounts, int> value) =>
            groups.Select(group => list.Where(row => row.Group == group).Sum(value)).ToList();
        var totals = Sum(row => row.Assignments);
        var completed = Sum(row => row.Completed);
        var hiddenTotals = Suppression.Apply(totals, Sum(row => row.Members), minimum);
        var hiddenCompleted = Suppression.Apply(completed, Sum(row => row.MembersCompleted), minimum);
        return groups.Select((group, index) =>
        {
            var suppressed = hiddenTotals[index] || hiddenCompleted[index];
            return new DemographicRowResponse(
                group,
                hiddenTotals[index] ? null : totals[index],
                hiddenCompleted[index] ? null : completed[index],
                suppressed ? null : Rate(completed[index], totals[index]),
                suppressed);
        }).ToList();
    }

    private static string EmploymentGroup(EmploymentStatus? status) =>
        status is { } value ? JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()) : NotRecorded;

    private sealed record GroupCounts(string Group, int Assignments, int Completed, int Members, int MembersCompleted);
}
