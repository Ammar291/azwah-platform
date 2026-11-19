namespace FamilyResilience.Application.Sessions;

/// <summary>
/// What one run of <see cref="ExpireOverdueAssignments"/> closed. <see cref="Failures"/> holds error codes only (a
/// fault as <c>exception:&lt;type&gt;</c>); those assignments were rolled back and are tried again at the next run.
/// </summary>
public sealed record OverdueAssignmentsReport(int AssignmentsExpired, int SessionsAbandoned, IReadOnlyList<string> Failures);
