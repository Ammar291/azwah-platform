namespace FamilyResilience.Domain.Sessions;

public enum SubmissionOutcome
{
    Submitted = 1,

    /// <summary>The session was already submitted or scored; nothing changed.</summary>
    AlreadySubmitted = 2,
}
