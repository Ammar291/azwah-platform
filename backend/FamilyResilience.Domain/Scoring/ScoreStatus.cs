namespace FamilyResilience.Domain.Scoring;

public enum ScoreStatus
{
    Complete = 1,

    /// <summary>The approved policy could not produce a score (for example too many non-substantive answers).</summary>
    Incomplete = 2,
}
