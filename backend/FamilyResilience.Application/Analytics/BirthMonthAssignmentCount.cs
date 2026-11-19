namespace FamilyResilience.Application.Analytics;

/// <summary>
/// The non-cancelled assignments of the members born in one month (a query row, never an individual value): how many
/// there are and how many are completed, and how many distinct members hold them and hold a completed one. One member
/// can hold several assignments, so the member counts decide small-cell suppression (D-58).
/// </summary>
public sealed record BirthMonthAssignmentCount(
    int BirthYear, int BirthMonth, int Assignments, int Completed, int Members, int MembersCompleted);
