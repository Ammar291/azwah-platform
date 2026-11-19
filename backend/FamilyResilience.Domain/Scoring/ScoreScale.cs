namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// The totals an approved rule set can record, lowest to highest, and its tier bands in order (empty when it has none).
/// Read from the approved parameters and the version's content, never assumed.
/// </summary>
public sealed record ScoreScale(int Minimum, int Maximum, IReadOnlyList<TierBand> Bands)
{
    /// <summary>Whether totals on both scales can share one axis: the same range and the same bands, in order.</summary>
    public bool Matches(ScoreScale other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Minimum == other.Minimum && Maximum == other.Maximum && Bands.SequenceEqual(other.Bands);
    }
}
