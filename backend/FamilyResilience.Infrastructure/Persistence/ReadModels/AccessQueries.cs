using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>Untracked reads for access decisions, so nothing they load can be saved by accident.</summary>
public sealed class AccessQueries(FamilyResilienceDbContext dbContext) : IAccessQueries
{
    public async Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.PlatformUsers.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Subject == subject, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var household = await dbContext.Households.AsNoTracking()
            .Include(candidate => candidate.Members)
            .Include(candidate => candidate.Memberships)
            .AsSplitQuery()
            .Where(candidate => candidate.Memberships.Any(
                membership => membership.PlatformUserId == user.Id && membership.Status == MembershipStatus.Active))
            .SingleOrDefaultAsync(cancellationToken);
        return new CallerRecord(user.Id, household);
    }

    public async Task<FamilyMemberId?> AssignmentMemberAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default)
    {
        var members = await dbContext.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignment.Id == assignmentId)
            .Select(assignment => assignment.FamilyMemberId)
            .ToListAsync(cancellationToken);
        return members.Count == 0 ? null : members[0];
    }

    public async Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        var members = await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.FamilyMemberId)
            .ToListAsync(cancellationToken);
        return members.Count == 0 ? null : members[0];
    }

    public Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default) =>
        dbContext.ExportJobs.AsNoTracking()
            .Where(job => job.Id == exportJobId)
            .Select(job => job.RequestedBy)
            .SingleOrDefaultAsync(cancellationToken);
}
