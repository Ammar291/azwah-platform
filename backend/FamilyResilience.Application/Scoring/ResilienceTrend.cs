using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// Which check-ins the family's trend chart draws (D-132). Only recorded totals are drawn, exactly as recorded; nothing
/// is computed from them. The axis is the scale of the latest recorded total, so a total on another scale (a rule set
/// with another range or other bands) is left out rather than drawn against bands it was not scored with. Points join
/// only while the same rule set scored them one after another: totals from different rule sets may not be comparable.
/// </summary>
public static class ResilienceTrend
{
    /// <summary>About twenty years of check-ins six months apart; older ones are not read.</summary>
    public const int MaxCheckIns = 40;

    public static ResilienceTrendResponse HeadOfHouseholdOnly { get; } =
        new(ScoreVisibility.HeadOfHouseholdOnly, null, [], 0);

    public static ResilienceTrendResponse Build(
        IReadOnlyList<ResilienceCheckIn> checkIns, IReadOnlyDictionary<RuleSetId, ScoreScale> scales)
    {
        ArgumentNullException.ThrowIfNull(checkIns);
        ArgumentNullException.ThrowIfNull(scales);

        var ordered = checkIns.OrderBy(checkIn => checkIn.SubmittedAt).ThenBy(checkIn => checkIn.SessionId).ToList();
        ScoreScale? ScaleOf(ResilienceCheckIn checkIn) =>
            checkIn is { ResultStatus: ScoreStatus.Complete, TotalScore: not null, RuleSetId: { } ruleSetId }
                ? scales.GetValueOrDefault(ruleSetId)
                : null;

        var axis = ordered.Select(ScaleOf).LastOrDefault(scale => scale is not null);
        if (axis is null)
        {
            return new ResilienceTrendResponse(ScoreVisibility.Visible, null, [], ordered.Count);
        }

        var points = new List<ResilienceTrendPointResponse>();
        RuleSetId? previous = null;
        var segment = -1;
        foreach (var checkIn in ordered)
        {
            if (ScaleOf(checkIn) is not { } scale || !scale.Matches(axis))
            {
                continue;
            }

            if (checkIn.RuleSetId != previous)
            {
                segment++;
                previous = checkIn.RuleSetId;
            }

            points.Add(new ResilienceTrendPointResponse(
                checkIn.SessionId, checkIn.WaveCode, checkIn.SubmittedAt, checkIn.TotalScore!.Value, checkIn.TierCode, segment));
        }

        return new ResilienceTrendResponse(
            ScoreVisibility.Visible,
            ScoreScaleResponse.From(axis),
            points,
            ordered.Count - points.Count);
    }
}
