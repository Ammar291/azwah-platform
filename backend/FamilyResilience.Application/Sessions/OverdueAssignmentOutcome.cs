namespace FamilyResilience.Application.Sessions;

/// <summary>What <see cref="ExpireOverdueAssignment"/> did with one assignment.</summary>
public enum OverdueAssignmentOutcome
{
    /// <summary>Nothing: it was finished meanwhile, or is not past its expiry.</summary>
    Unchanged = 0,

    /// <summary>It had no session in progress and is now Expired.</summary>
    Expired = 1,

    /// <summary>Its session in progress is now Abandoned and the assignment Expired.</summary>
    SessionAbandonedAndExpired = 2,
}
