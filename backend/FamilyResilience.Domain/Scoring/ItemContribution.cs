using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>One scored item: its raw answer and the calculated value, or null when the answer was excluded as non-substantive.</summary>
public sealed record ItemContribution(InstrumentItemId ItemId, int RawValue, decimal? CalculatedValue, string? Category)
{
    public bool IsExcluded => CalculatedValue is null;
}
