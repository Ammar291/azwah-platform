using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// DEMONSTRATIONS ONLY (D-130). One answer for every item in a scoring scope, chosen so that the approved
/// <c>summed_item_values</c> rules give a total in the band a demonstration asked for: the lowest band, the highest
/// band, or for <see cref="DemoScoreLevel.Medium"/> the band holding the middle of the possible totals. The total aimed
/// at is the middle of that band (rounded down); with no bands it is the lowest, middle or highest possible total. The
/// points above each item's lowest value are spread evenly across the items, reverse items are answered on the mirrored
/// side of their scale, items that are not scored take their first substantive option, and a non-substantive answer is
/// never chosen. Nothing here scores: the plan's answers are scored like any others once submitted.
/// </summary>
public sealed record DemoAnswerPlan(int Total, string? TierCode, IReadOnlyList<ScoringResponse> Answers)
{
    /// <summary>
    /// The plan for <paramref name="content"/> under <paramref name="parameters"/>, which the strategy has already
    /// validated against that content (every direction specified, every scored scale consecutive).
    /// </summary>
    public static DemoAnswerPlan Create(ScoringContent content, SummedItemValuesParameters parameters, DemoScoreLevel level)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(parameters);

        var minimum = content.MinimumTotal();
        var maximum = content.MaximumTotal();
        var middle = minimum + ((maximum - minimum) / 2);
        var band = parameters.TierBands.Count == 0
            ? null
            : level switch
            {
                DemoScoreLevel.Low => parameters.TierBands.MinBy(candidate => candidate.Min),
                DemoScoreLevel.High => parameters.TierBands.MaxBy(candidate => candidate.Max),
                DemoScoreLevel.Medium => parameters.TierBands.Single(candidate => candidate.Min <= middle && middle <= candidate.Max),
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown demonstration level."),
            };
        var target = band is not null
            ? band.Min + ((band.Max - band.Min) / 2)
            : level switch
            {
                DemoScoreLevel.Low => minimum,
                DemoScoreLevel.Medium => middle,
                DemoScoreLevel.High => maximum,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown demonstration level."),
            };

        var calculated = Spread(content.ScoredItems(), target - minimum);
        var answers = content.Items
            .Select(item => item.IsScored ? Scored(item, calculated[item.ItemId]) : Unscored(item))
            .ToList();
        return new DemoAnswerPlan(target, band?.Code, answers);
    }

    /// <summary>
    /// Each scored item's calculated value: its lowest value plus an even share of <paramref name="extra"/> (the shares
    /// differ by at most one and alternate along the items), then any remainder a shorter scale could not take.
    /// </summary>
    private static Dictionary<InstrumentItemId, int> Spread(IReadOnlyList<ScoringItem> items, int extra)
    {
        var shares = new int[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            var share = (int)(((long)extra * (index + 1) / items.Count) - ((long)extra * index / items.Count));
            shares[index] = Math.Min(share, items[index].ScaleMaximum() - items[index].ScaleMinimum());
        }

        var left = extra - shares.Sum();
        for (var index = 0; index < items.Count && left > 0; index++)
        {
            var room = items[index].ScaleMaximum() - items[index].ScaleMinimum() - shares[index];
            var added = Math.Min(room, left);
            shares[index] += added;
            left -= added;
        }

        if (left != 0)
        {
            throw new InvalidOperationException("The target total lies outside the totals this scope can produce.");
        }

        return items.Select((item, index) => (item.ItemId, Value: item.ScaleMinimum() + shares[index]))
            .ToDictionary(pair => pair.ItemId, pair => pair.Value);
    }

    private static ScoringResponse Scored(ScoringItem item, int calculatedValue)
    {
        var raw = item.Direction == ScoringDirection.Reverse
            ? item.ScaleMinimum() + item.ScaleMaximum() - calculatedValue
            : calculatedValue;
        var option = item.Options.First(candidate => !candidate.IsNonSubstantive && candidate.Value == raw);
        return new ScoringResponse(item.ItemId, option.OptionId, option.Value);
    }

    private static ScoringResponse Unscored(ScoringItem item)
    {
        var option = item.Options.FirstOrDefault(candidate => !candidate.IsNonSubstantive) ?? item.Options[0];
        return new ScoringResponse(item.ItemId, option.OptionId, option.Value);
    }
}
