using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class AssessmentSessionRepository(FamilyResilienceDbContext dbContext) : IAssessmentSessionRepository
{
    public void Add(AssessmentSession session) => dbContext.AssessmentSessions.Add(session);

    public Task<AssessmentSession?> GetAsync(AssessmentSessionId id, CancellationToken cancellationToken = default) =>
        dbContext.AssessmentSessions
            .Include(session => session.Responses)
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<AssessmentSession?> GetLiveForAssignmentAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        dbContext.AssessmentSessions
            .Include(session => session.Responses)
            .SingleOrDefaultAsync(
                session => session.AssessmentAssignmentId == assignmentId && session.Status != SessionStatus.Abandoned,
                cancellationToken);

    public async Task<AssessmentSessionId?> FindLiveIdForAssignmentAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default) =>
        await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => session.AssessmentAssignmentId == assignmentId && session.Status != SessionStatus.Abandoned)
            .Select(session => (AssessmentSessionId?)session.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AssessmentSessionId>> ListInProgressIdsForMemberAsync(
        FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => session.FamilyMemberId == memberId && session.Status == SessionStatus.InProgress)
            .OrderBy(session => session.Id)
            .Select(session => session.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AssessmentSession>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        await dbContext.AssessmentSessions.AsNoTracking()
            .Include(session => session.Responses)
            .Where(session => memberIds.Contains(session.FamilyMemberId))
            .OrderBy(session => session.StartedAt)
            .ThenBy(session => session.Id)
            .ToListAsync(cancellationToken);
}
