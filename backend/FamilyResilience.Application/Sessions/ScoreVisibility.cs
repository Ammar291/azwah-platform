namespace FamilyResilience.Application.Sessions;

/// <summary>Whether a participant response carries the score values, decided on the server from the caller (D-103).</summary>
public enum ScoreVisibility
{
    /// <summary>The caller signs in as the household's head: totals, tiers and category scores are included.</summary>
    Visible = 1,

    /// <summary>The caller is not the head: no total, tier or category score is included.</summary>
    HeadOfHouseholdOnly = 2,
}
