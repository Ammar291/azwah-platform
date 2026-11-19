using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// The non-cancelled assignments of the members with one employment status (a query row, never an individual value):
/// how many there are and how many are completed, and how many distinct members hold them and hold a completed one.
/// </summary>
public sealed record EmploymentAssignmentCount(
    EmploymentStatus? EmploymentStatus, int Assignments, int Completed, int Members, int MembersCompleted);
