using FamilyResilience.Application.Assignments;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class AssessmentAssignmentRepository(FamilyResilienceDbContext dbContext) : IAssessmentAssignmentRepository
{
    public void Add(AssessmentAssignment assignment) => dbContext.AssessmentAssignments.Add(assignment);

    public Task<AssessmentAssignment?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default) =>
        dbContext.AssessmentAssignments.SingleOrDefaultAsync(assignment => assignment.Id == id, cancellationToken);

    public Task<bool> ExistsLiveAsync(
        FamilyMemberId memberId,
        InstrumentVersionId versionId,
        InstrumentSectionId? sectionId,
        WaveId waveId,
        CancellationToken cancellationToken = default) =>
        dbContext.AssessmentAssignments.AnyAsync(
            assignment => assignment.FamilyMemberId == memberId
                && assignment.InstrumentVersionId == versionId
                && assignment.InstrumentSectionId == sectionId
                && assignment.WaveId == waveId
                && assignment.Status != AssignmentStatus.Cancelled
                && assignment.Status != AssignmentStatus.Expired,
            cancellationToken);

    public async Task<IReadOnlyList<AssessmentAssignment>> ListForMemberAsync(
        FamilyMemberId memberId, WaveId waveId, CancellationToken cancellationToken = default) =>
        await dbContext.AssessmentAssignments
            .Where(assignment => assignment.FamilyMemberId == memberId && assignment.WaveId == waveId)
            .OrderBy(assignment => assignment.AssignedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssessmentAssignment>> ListOpenForMemberAsync(
        FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        await dbContext.AssessmentAssignments
            .Where(assignment => assignment.FamilyMemberId == memberId
                && (assignment.Status == AssignmentStatus.Assigned || assignment.Status == AssignmentStatus.InProgress))
            .OrderBy(assignment => assignment.AssignedAt)
            .ThenBy(assignment => assignment.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssessmentAssignmentId>> ListOverdueIdsAsync(
        DateTimeOffset now, int limit, IReadOnlyCollection<AssessmentAssignmentId> excluding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(excluding);
        var overdue = dbContext.AssessmentAssignments.AsNoTracking()
            .Where(assignment => (assignment.Status == AssignmentStatus.Assigned || assignment.Status == AssignmentStatus.InProgress)
                && assignment.ExpiresAt <= now);
        if (excluding.Count > 0)
        {
            overdue = overdue.Where(assignment => !excluding.Contains(assignment.Id));
        }

        return await overdue
            .OrderBy(assignment => assignment.ExpiresAt)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => assignment.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
