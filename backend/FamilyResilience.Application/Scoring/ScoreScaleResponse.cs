using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// The grading scale a total was scored on: the lowest and highest totals possible and the bands between them, lowest
/// first, as the approved rule set states them. The session result and the score report carry it (D-111), and so does
/// the family's trend chart (D-132). Clients draw it; they never decide a band from it.
/// </summary>
public sealed record ScoreScaleResponse(int Minimum, int Maximum, IReadOnlyList<ScoreBandResponse> Bands)
{
    public static ScoreScaleResponse From(ScoreScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return new ScoreScaleResponse(scale.Minimum, scale.Maximum, [.. scale.Bands.Select(band => new ScoreBandResponse(band.Code, band.Min, band.Max))]);
    }
}
