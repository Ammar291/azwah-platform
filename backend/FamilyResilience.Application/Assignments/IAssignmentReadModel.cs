using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// Read-only projections of assignments joined with their version, instrument, section, wave and live session.
/// Implemented in Infrastructure; callers pass only members the caller may act for.
/// </summary>
public interface IAssignmentReadModel
{
    /// <summary>One page of the members' assignments, newest first, optionally of one status.</summary>
    Task<IReadOnlyList<ParticipantAssignmentResponse>> ListAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, CancellationToken cancellationToken = default);

    Task<ParticipantAssignmentResponse?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default);
}
