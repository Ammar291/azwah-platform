using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Closes what a member leaving the roster left open: each of their sessions still in progress is abandoned (its answers
/// stay as history and are never scored) and each of their assignments still waiting or in progress is cancelled, with
/// the audit event each transition writes elsewhere. Submitted and scored sessions and finished assignments stay as they
/// are. Call it inside the caller's explicit transaction, after the change that removes the member is committed: it
/// locks the sessions first, as saving answers and submitting do, then commits the sessions and then the assignments,
/// so a failure in either rolls the whole removal back.
/// </summary>
public interface IMemberAssessmentCloser
{
    Task<Result<MemberAssessmentsClosed>> CloseAsync(FamilyMemberId memberId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
