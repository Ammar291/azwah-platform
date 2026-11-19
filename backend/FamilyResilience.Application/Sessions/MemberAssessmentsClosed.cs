namespace FamilyResilience.Application.Sessions;

/// <summary>What <see cref="IMemberAssessmentCloser"/> closed for one member.</summary>
public sealed record MemberAssessmentsClosed(int SessionsAbandoned, int AssignmentsCancelled);
