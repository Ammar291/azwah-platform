using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Assignments;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

public sealed partial class AnalyticsQueries
{
    public async Task<IReadOnlyList<StatusCount>> AssignmentStatusesAsync(WaveId? waveId, CancellationToken cancellationToken = default)
    {
        var rows = await Assignments(waveId)
            .GroupBy(assignment => assignment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new StatusCount(row.Status, row.Count)).ToList();
    }

    public Task<int> StartedAssignmentsAsync(WaveId? waveId, CancellationToken cancellationToken = default) =>
        Assignments(waveId).CountAsync(
            assignment => dbContext.AssessmentSessions.Any(session => session.AssessmentAssignmentId == assignment.Id),
            cancellationToken);

    public async Task<IReadOnlyList<VersionStatusCount>> AssignmentStatusesByVersionAsync(
        WaveId? waveId, CancellationToken cancellationToken = default)
    {
        var rows = await Assignments(waveId)
            .Join(dbContext.InstrumentVersions, assignment => assignment.InstrumentVersionId, version => version.Id,
                (assignment, version) => new { assignment.InstrumentSectionId, assignment.Status, version.VersionNumber, version.InstrumentId })
            .Join(dbContext.Instruments, row => row.InstrumentId, instrument => instrument.Id,
                (row, instrument) => new { row.InstrumentSectionId, row.Status, row.VersionNumber, instrument.Code })
            .GroupBy(row => new { row.Code, row.VersionNumber, row.InstrumentSectionId, row.Status })
            .Select(group => new { group.Key.Code, group.Key.VersionNumber, group.Key.InstrumentSectionId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var sections = await SectionCodesAsync(rows.Select(row => row.InstrumentSectionId), cancellationToken);
        return rows
            .Select(row => new VersionStatusCount(
                row.Code, row.VersionNumber, row.InstrumentSectionId is { } id ? sections[id] : null, row.Status, row.Count))
            .ToList();
    }

    public async Task<IReadOnlyList<DayCount>> CompletionsByDayAsync(
        WaveId? waveId, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        var completedAt = await Assignments(waveId)
            .Where(assignment => assignment.Status == AssignmentStatus.Completed
                && assignment.CompletedAt >= from
                && assignment.CompletedAt < until)
            .Select(assignment => assignment.CompletedAt!.Value)
            .GroupBy(time => new { time.Year, time.Month, time.Day })
            .Select(group => new { group.Key.Year, group.Key.Month, group.Key.Day, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return completedAt
            .Select(row => new DayCount(new DateOnly(row.Year, row.Month, row.Day), row.Count))
            .OrderBy(row => row.Day)
            .ToList();
    }
}
