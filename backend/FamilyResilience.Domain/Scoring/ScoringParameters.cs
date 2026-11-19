namespace FamilyResilience.Domain.Scoring;

/// <summary>Parameters a strategy has validated against the content it will score. Each strategy has its own type.</summary>
public abstract record ScoringParameters
{
    /// <summary>The scale of the total these parameters record for <paramref name="content"/>, or null when they record none.</summary>
    public virtual ScoreScale? TotalScale(ScoringContent content) => null;
}
