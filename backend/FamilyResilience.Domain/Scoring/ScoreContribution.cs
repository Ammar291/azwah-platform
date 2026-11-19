using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>One item's part of a result: the raw answer and the calculated value, or none when the answer was excluded.</summary>
public sealed class ScoreContribution : Entity<ScoreContributionId>
{
    private ScoreContribution(ScoreContributionId id)
        : base(id)
    {
    }

    public ScoreResultId ScoreResultId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public InstrumentItemId InstrumentItemId { get; private set; }

    public int RawValue { get; private set; }

    /// <summary>Null when the answer was non-substantive and excluded.</summary>
    public decimal? CalculatedValue { get; private set; }

    public bool IsExcluded { get; private set; }

    public string? ScoringCategory { get; private set; }

    internal static ScoreContribution Create(ScoreResultId resultId, InstrumentVersionId versionId, ItemContribution contribution) =>
        new(ScoreContributionId.New())
        {
            ScoreResultId = resultId,
            InstrumentVersionId = versionId,
            InstrumentItemId = contribution.ItemId,
            RawValue = contribution.RawValue,
            CalculatedValue = contribution.CalculatedValue,
            IsExcluded = contribution.IsExcluded,
            ScoringCategory = contribution.Category,
        };
}
