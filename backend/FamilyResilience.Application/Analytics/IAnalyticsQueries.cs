using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Fixed aggregate queries for the analytics overview, counted in PostgreSQL. Callers choose only the wave and the time
/// window; no query takes a column, an expression or SQL from a request. Rows carry counts, never individual values.
/// "Active members" are the active members of households that have not withdrawn.
/// </summary>
public interface IAnalyticsQueries
{
    Task<EnrollmentCounts> EnrollmentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StatusCount>> AssignmentStatusesAsync(WaveId? waveId, CancellationToken cancellationToken = default);

    /// <summary>Assignments with at least one session of any status.</summary>
    Task<int> StartedAssignmentsAsync(WaveId? waveId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VersionStatusCount>> AssignmentStatusesByVersionAsync(WaveId? waveId, CancellationToken cancellationToken = default);

    /// <summary>Completed assignments per UTC day from <paramref name="from"/> up to, not including, <paramref name="until"/>.</summary>
    Task<IReadOnlyList<DayCount>> CompletionsByDayAsync(
        WaveId? waveId, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken = default);

    /// <summary>Non-cancelled assignments and their distinct members by the member's birth year and month.</summary>
    Task<IReadOnlyList<BirthMonthAssignmentCount>> AssignmentsByBirthMonthAsync(WaveId? waveId, CancellationToken cancellationToken = default);

    /// <summary>Non-cancelled assignments and their distinct members by the member's employment status.</summary>
    Task<IReadOnlyList<EmploymentAssignmentCount>> AssignmentsByEmploymentAsync(WaveId? waveId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Complete score results with a tier, by scoring rule set and tier: only rule sets that are still Approved, and only
    /// members whose research-use consent is active now, in households that have not withdrawn. Each row also carries
    /// the distinct members behind its tier and behind its whole distribution.
    /// </summary>
    Task<IReadOnlyList<TierCount>> TierCountsAsync(WaveId? waveId, CancellationToken cancellationToken = default);
}
