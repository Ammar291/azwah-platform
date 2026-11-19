namespace FamilyResilience.Domain.Questionnaires;

public enum ScoringDirection
{
    Forward = 1,
    Reverse = 2,

    /// <summary>The approved specification excludes the item from every score.</summary>
    NotScored = 3,

    /// <summary>
    /// No approved direction yet. A version may be published for data collection with it, but no scoring rule set can
    /// cover the item until a new version states its direction.
    /// </summary>
    Unspecified = 4,
}
