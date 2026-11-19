namespace FamilyResilience.Domain.Assignments;

/// <summary>The only status changes an assignment may make. Every other change is refused.</summary>
public static class AssignmentTransitions
{
    private static readonly HashSet<(AssignmentStatus From, AssignmentStatus To)> Allowed =
    [
        (AssignmentStatus.Assigned, AssignmentStatus.InProgress),
        (AssignmentStatus.InProgress, AssignmentStatus.Assigned),
        (AssignmentStatus.InProgress, AssignmentStatus.Completed),
        (AssignmentStatus.Assigned, AssignmentStatus.Cancelled),
        (AssignmentStatus.InProgress, AssignmentStatus.Cancelled),
        (AssignmentStatus.Assigned, AssignmentStatus.Expired),
        (AssignmentStatus.InProgress, AssignmentStatus.Expired),
    ];

    public static bool IsAllowed(AssignmentStatus from, AssignmentStatus to) => Allowed.Contains((from, to));
}
