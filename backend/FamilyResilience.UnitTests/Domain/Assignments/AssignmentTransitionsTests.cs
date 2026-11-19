using FamilyResilience.Domain.Assignments;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments;

public sealed class AssignmentTransitionsTests
{
    private static readonly HashSet<(AssignmentStatus, AssignmentStatus)> Expected =
    [
        (AssignmentStatus.Assigned, AssignmentStatus.InProgress),
        (AssignmentStatus.InProgress, AssignmentStatus.Assigned),
        (AssignmentStatus.InProgress, AssignmentStatus.Completed),
        (AssignmentStatus.Assigned, AssignmentStatus.Cancelled),
        (AssignmentStatus.InProgress, AssignmentStatus.Cancelled),
        (AssignmentStatus.Assigned, AssignmentStatus.Expired),
        (AssignmentStatus.InProgress, AssignmentStatus.Expired),
    ];

    [Fact]
    public void Exactly_the_documented_moves_are_allowed()
    {
        foreach (var from in Enum.GetValues<AssignmentStatus>())
        {
            foreach (var to in Enum.GetValues<AssignmentStatus>())
            {
                Assert.True(
                    Expected.Contains((from, to)) == AssignmentTransitions.IsAllowed(from, to),
                    $"{from} -> {to} should be {(Expected.Contains((from, to)) ? "allowed" : "refused")}.");
            }
        }
    }

    [Fact]
    public void A_completed_assignment_can_never_be_cancelled()
    {
        var (_, _, assignment) = SubmittedSession();

        Assert.Equal(AssignmentErrors.AlreadyFinished, assignment.Cancel(Now).Error);
        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
    }
}
