using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>An item as the scoring strategy sees it: direction, category and options, copied from the approved version.</summary>
public sealed record ScoringItem(
    InstrumentItemId ItemId, string Code, string? Category, ScoringDirection Direction, IReadOnlyList<ScoringOption> Options)
{
    /// <summary>Whether the version requires an answer before submission. Defaults to true for content built in code.</summary>
    public bool IsRequired { get; init; } = true;

    public bool IsScored => Direction is ScoringDirection.Forward or ScoringDirection.Reverse;

    /// <summary>The values that are points on the scale, ascending (a non-substantive value such as 99 is left out).</summary>
    public IReadOnlyList<int> SubstantiveValues() =>
        Options.Where(option => !option.IsNonSubstantive).Select(option => option.Value).Order().ToList();

    public bool HasConsecutiveScale()
    {
        var values = SubstantiveValues();
        return values.Count >= 2 && values[^1] - values[0] == values.Count - 1;
    }

    public int ScaleMinimum() => SubstantiveValues()[0];

    public int ScaleMaximum() => SubstantiveValues()[^1];

    /// <summary>What a substantive answer counts: its value, mirrored on the scale for a reverse-scored item.</summary>
    public decimal PointsFor(int rawValue) =>
        Direction == ScoringDirection.Reverse ? ScaleMinimum() + ScaleMaximum() - rawValue : rawValue;
}
