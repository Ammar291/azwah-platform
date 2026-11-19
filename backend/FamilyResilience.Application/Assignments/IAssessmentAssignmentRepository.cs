using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

public interface IAssessmentAssignmentRepository
{
    void Add(AssessmentAssignment assignment);

    Task<AssessmentAssignment?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default);

    /// <summary>Whether a non-cancelled, non-expired assignment already exists (the database enforces the same rule).</summary>
    Task<bool> ExistsLiveAsync(
        FamilyMemberId memberId,
        InstrumentVersionId versionId,
        InstrumentSectionId? sectionId,
        WaveId waveId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssessmentAssignment>> ListForMemberAsync(
        FamilyMemberId memberId, WaveId waveId, CancellationToken cancellationToken = default);

    /// <summary>The member's assignments still waiting or in progress, in every wave, tracked for changes.</summary>
    Task<IReadOnlyList<AssessmentAssignment>> ListOpenForMemberAsync(
        FamilyMemberId memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The assignments still waiting or in progress whose expiry is at or before <paramref name="now"/>, the longest
    /// overdue first, at most <paramref name="limit"/>, leaving out <paramref name="excluding"/> (those a run has already
    /// tried, so one that keeps failing cannot hold back the rest). Only identifiers, read without tracking.
    /// </summary>
    Task<IReadOnlyList<AssessmentAssignmentId>> ListOverdueIdsAsync(
        DateTimeOffset now, int limit, IReadOnlyCollection<AssessmentAssignmentId> excluding, CancellationToken cancellationToken = default);
}
