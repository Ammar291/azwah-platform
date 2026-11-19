using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

public interface IAssessmentSessionRepository
{
    void Add(AssessmentSession session);

    /// <summary>The session with all its responses.</summary>
    Task<AssessmentSession?> GetAsync(AssessmentSessionId id, CancellationToken cancellationToken = default);

    /// <summary>The assignment's session that is not abandoned, if any (at most one exists).</summary>
    Task<AssessmentSession?> GetLiveForAssignmentAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The identifier of the assignment's session that is not abandoned, if any, read without tracking, so the session
    /// can be locked before it is loaded.
    /// </summary>
    Task<AssessmentSessionId?> FindLiveIdForAssignmentAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The identifiers of the member's sessions still in progress, in identifier order, read without tracking, so they
    /// can be locked (always in the same order) before they are loaded.
    /// </summary>
    Task<IReadOnlyList<AssessmentSessionId>> ListInProgressIdsForMemberAsync(
        FamilyMemberId memberId, CancellationToken cancellationToken = default);

    /// <summary>Every session of the members, abandoned ones included, with their responses, oldest first, read without tracking.</summary>
    Task<IReadOnlyList<AssessmentSession>> ListForMembersAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default);
}
