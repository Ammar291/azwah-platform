using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

public sealed partial class AnalyticsQueries
{
    // One member can hold several assignments (a module each, and one per wave), so every cell is counted twice: its
    // assignments, which the overview shows, and the distinct members behind them, which decide suppression (D-58).
    public async Task<IReadOnlyList<BirthMonthAssignmentCount>> AssignmentsByBirthMonthAsync(
        WaveId? waveId, CancellationToken cancellationToken = default)
    {
        var rows = await LiveAssignments(waveId)
            .Join(dbContext.Set<FamilyMember>(), assignment => assignment.FamilyMemberId, member => member.Id,
                (assignment, member) => new { assignment.Status, assignment.FamilyMemberId, member.BirthMonth.Year, member.BirthMonth.Month })
            .GroupBy(row => new { row.Year, row.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Assignments = group.Count(),
                Completed = group.Count(row => row.Status == AssignmentStatus.Completed),
                Members = group.Select(row => row.FamilyMemberId).Distinct().Count(),
                MembersCompleted = group.Where(row => row.Status == AssignmentStatus.Completed)
                    .Select(row => row.FamilyMemberId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new BirthMonthAssignmentCount(row.Year, row.Month, row.Assignments, row.Completed, row.Members, row.MembersCompleted))
            .ToList();
    }

    public async Task<IReadOnlyList<EmploymentAssignmentCount>> AssignmentsByEmploymentAsync(
        WaveId? waveId, CancellationToken cancellationToken = default)
    {
        var rows = await LiveAssignments(waveId)
            .Join(dbContext.Set<FamilyMember>(), assignment => assignment.FamilyMemberId, member => member.Id,
                (assignment, member) => new { assignment.Status, assignment.FamilyMemberId, member.EmploymentStatus })
            .GroupBy(row => row.EmploymentStatus)
            .Select(group => new
            {
                EmploymentStatus = group.Key,
                Assignments = group.Count(),
                Completed = group.Count(row => row.Status == AssignmentStatus.Completed),
                Members = group.Select(row => row.FamilyMemberId).Distinct().Count(),
                MembersCompleted = group.Where(row => row.Status == AssignmentStatus.Completed)
                    .Select(row => row.FamilyMemberId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new EmploymentAssignmentCount(row.EmploymentStatus, row.Assignments, row.Completed, row.Members, row.MembersCompleted))
            .ToList();
    }

    /// <summary>Assignments that were not cancelled, the denominator of every completion rate.</summary>
    private IQueryable<AssessmentAssignment> LiveAssignments(WaveId? waveId) =>
        Assignments(waveId).Where(assignment => assignment.Status != AssignmentStatus.Cancelled);
}
