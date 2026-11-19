namespace FamilyResilience.Domain.Scoring;

public static class ScoreIncompleteReasons
{
    /// <summary>More of the scored items were answered non-substantively (99) than the approved share allows.</summary>
    public const string NonSubstantiveShareExceeded = "non_substantive_share_exceeded";

    /// <summary>The total or a category had no substantive answer at all.</summary>
    public const string NoSubstantiveAnswers = "no_substantive_answers";
}
