using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Identity;

/// <summary>Untracked reads that access decisions need. Implemented in Infrastructure.</summary>
public interface IAccessQueries
{
    /// <summary>The platform user for <paramref name="subject"/> with the household of their active membership.</summary>
    Task<CallerRecord?> FindCallerAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>The member an assignment belongs to, or null when the assignment does not exist.</summary>
    Task<FamilyMemberId?> AssignmentMemberAsync(AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default);

    /// <summary>The member a session belongs to, or null when the session does not exist.</summary>
    Task<FamilyMemberId?> SessionMemberAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>The token subject that requested an export, or null when the export does not exist.</summary>
    Task<string?> ExportRequesterAsync(ExportJobId exportJobId, CancellationToken cancellationToken = default);
}
