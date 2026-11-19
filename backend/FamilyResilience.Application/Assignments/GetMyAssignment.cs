using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Assignments;

/// <summary>One assignment of a member the caller may act for; any other answers "not found".</summary>
public sealed class GetMyAssignment(ICallerAccess access, IAssignmentReadModel assignments)
{
    public async Task<Result<ParticipantAssignmentResponse>> HandleAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetAsync(AssessmentAssignmentId.From(assignmentId), cancellationToken);
        return assignment is not null && await access.CanActForMemberAsync(FamilyMemberId.From(assignment.MemberId), cancellationToken)
            ? Result.Success(assignment)
            : Result.Failure<ParticipantAssignmentResponse>(AssignmentErrors.AssignmentNotFound);
    }
}
