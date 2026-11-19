namespace FamilyResilience.Application.Sessions;

/// <summary>Where a submitted session's result stands.</summary>
public enum ResultState
{
    /// <summary>Submitted, but no approved scoring rule set has scored it yet.</summary>
    Pending = 1,

    /// <summary>Scored, with a total and tier where the approved rules define them.</summary>
    Complete = 2,

    /// <summary>Scored, but the approved rules could not classify it (for example too many answers declined).</summary>
    Incomplete = 3,
}
