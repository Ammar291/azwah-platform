namespace FamilyResilience.Domain.Sessions;

/// <summary>
/// The only status changes a session may make (the database trigger <c>trg_assessment_sessions_guarded</c> enforces the
/// same table). A submitted session never returns to InProgress; a retake or reassessment is a new session.
/// </summary>
public static class SessionTransitions
{
    private static readonly HashSet<(SessionStatus From, SessionStatus To)> Allowed =
    [
        (SessionStatus.InProgress, SessionStatus.Submitted),
        (SessionStatus.InProgress, SessionStatus.Abandoned),
        (SessionStatus.Submitted, SessionStatus.Scored),
        (SessionStatus.Scored, SessionStatus.Scored),
    ];

    public static bool IsAllowed(SessionStatus from, SessionStatus to) => Allowed.Contains((from, to));
}
