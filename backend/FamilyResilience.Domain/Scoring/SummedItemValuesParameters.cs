using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// The approved choices for the <c>summed_item_values</c> policy. Every choice is stated in the rule set; nothing has
/// a default, so an approval always records each decision explicitly.
/// </summary>
public sealed record SummedItemValuesParameters(
    bool Total, NonSubstantivePolicy? NonSubstantive, IReadOnlyList<TierBand> TierBands) : ScoringParameters
{
    public const string ReverseScoringMirrorScale = "mirror_scale";
    public const string UnansweredReject = "reject";
    public const string AggregationProratedMean = "prorated_mean";

    private static readonly string[] Keys = ["reverseScoring", "unanswered", "nonSubstantive", "total", "tierBands"];
    private static readonly string[] NonSubstantiveKeys = ["maxShare", "aggregation", "rounding"];
    private static readonly string[] RoundingKeys = ["mode", "decimals"];
    private static readonly string[] BandKeys = ["code", "min", "max"];

    /// <summary>
    /// From the lowest to the highest total the scored items can give, with the bands in order. A prorated total stays
    /// within the same range, so the scale holds with or without a non-substantive policy.
    /// </summary>
    public override ScoreScale? TotalScale(ScoringContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Total ? new ScoreScale(content.MinimumTotal(), content.MaximumTotal(), TierBands) : null;
    }

    public static Result<SummedItemValuesParameters> Read(JsonElement parameters, ScoringContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return Invalid<SummedItemValuesParameters>("$.parameters", "must be an object");
        }

        if (JsonObjects.FindUnexpectedKey(parameters, Keys) is { } unexpected)
        {
            return Invalid<SummedItemValuesParameters>($"$.parameters.{unexpected}", "is not a known parameter or appears twice");
        }

        if (!HasString(parameters, "reverseScoring", ReverseScoringMirrorScale))
        {
            return Invalid<SummedItemValuesParameters>("$.parameters.reverseScoring", $"must be \"{ReverseScoringMirrorScale}\"");
        }

        if (!HasString(parameters, "unanswered", UnansweredReject))
        {
            return Invalid<SummedItemValuesParameters>("$.parameters.unanswered", $"must be \"{UnansweredReject}\"");
        }

        if (!parameters.TryGetProperty("total", out var total) || total.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Invalid<SummedItemValuesParameters>("$.parameters.total", "must be true or false");
        }

        var items = CheckItems(content);
        if (items.IsFailure)
        {
            return Result.Failure<SummedItemValuesParameters>(items.Error!);
        }

        // "reject" refuses any unanswered scored item, while submission needs only required items: an optional scored
        // item would let a participant submit a session that can then never be scored.
        if (content.ScoredItems().FirstOrDefault(item => !item.IsRequired) is { } optional)
        {
            return Result.Failure<SummedItemValuesParameters>(ScoringErrors.OptionalItemScored with
            {
                Message = $"{ScoringErrors.OptionalItemScored.Message} Item: {optional.Code}.",
            });
        }

        // Without a total only category scores remain; with no categorised item the result would hold nothing at all.
        if (total.ValueKind == JsonValueKind.False && !content.ScoredItems().Any(item => item.Category is not null))
        {
            return Invalid<SummedItemValuesParameters>("$.parameters.total", "must be true: no scored item has a scoring category");
        }

        var offersNonSubstantive = content.ScoredItems().Any(item => item.Options.Any(option => option.IsNonSubstantive));
        NonSubstantivePolicy? policy = null;
        if (parameters.TryGetProperty("nonSubstantive", out var nonSubstantive))
        {
            if (!offersNonSubstantive)
            {
                return Invalid<SummedItemValuesParameters>(
                    "$.parameters.nonSubstantive", "is not allowed: no scored item offers a non-substantive answer");
            }

            var read = ReadNonSubstantive(nonSubstantive);
            if (read.IsFailure)
            {
                return Result.Failure<SummedItemValuesParameters>(read.Error!);
            }

            // Proration scales the mean to the item count, which is only a valid total when every scored item shares one scale.
            if (content.ScoredItems().Select(item => (item.ScaleMinimum(), item.ScaleMaximum())).Distinct().Count() > 1)
            {
                return Invalid<SummedItemValuesParameters>(
                    "$.parameters.nonSubstantive", "needs every scored item on the same substantive scale");
            }

            policy = read.Value;
        }
        else if (offersNonSubstantive)
        {
            return Invalid<SummedItemValuesParameters>(
                "$.parameters.nonSubstantive", "is required: scored items offer a non-substantive answer");
        }

        IReadOnlyList<TierBand> bands = [];
        if (parameters.TryGetProperty("tierBands", out var tierBands))
        {
            var read = ReadBands(tierBands, total.ValueKind == JsonValueKind.True, policy, content.MinimumTotal(), content.MaximumTotal());
            if (read.IsFailure)
            {
                return Result.Failure<SummedItemValuesParameters>(read.Error!);
            }

            bands = read.Value;
        }

        return Result.Success(new SummedItemValuesParameters(total.ValueKind == JsonValueKind.True, policy, bands));
    }

    private static Result CheckItems(ScoringContent content)
    {
        if (content.Items.FirstOrDefault(item => item.Direction == ScoringDirection.Unspecified) is { } unspecified)
        {
            return Result.Failure(About(ScoringErrors.ItemDirectionUnspecified, unspecified.Code));
        }

        var scored = content.ScoredItems();
        if (scored.Count == 0)
        {
            return Result.Failure(ScoringErrors.NoScoredItems);
        }

        return scored.FirstOrDefault(item => !item.HasConsecutiveScale()) is { } irregular
            ? Result.Failure(About(ScoringErrors.ItemScaleInvalid, irregular.Code))
            : Result.Success();
    }

    private static Result<NonSubstantivePolicy> ReadNonSubstantive(JsonElement element)
    {
        const string path = "$.parameters.nonSubstantive";
        if (element.ValueKind != JsonValueKind.Object)
        {
            return Invalid<NonSubstantivePolicy>(path, "must be an object");
        }

        if (JsonObjects.FindUnexpectedKey(element, NonSubstantiveKeys) is { } unexpected)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.{unexpected}", "is not a known key or appears twice");
        }

        if (!element.TryGetProperty("maxShare", out var share)
            || share.ValueKind != JsonValueKind.Number
            || !share.TryGetDecimal(out var maxShare)
            || maxShare is < 0 or > 1)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.maxShare", "must be a number from 0 to 1");
        }

        if (!HasString(element, "aggregation", AggregationProratedMean))
        {
            return Invalid<NonSubstantivePolicy>($"{path}.aggregation", $"must be \"{AggregationProratedMean}\"");
        }

        if (!element.TryGetProperty("rounding", out var rounding) || rounding.ValueKind != JsonValueKind.Object)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.rounding", "must be an object with mode and decimals");
        }

        if (JsonObjects.FindUnexpectedKey(rounding, RoundingKeys) is { } unexpectedRounding)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.rounding.{unexpectedRounding}", "is not a known key or appears twice");
        }

        MidpointRounding? mode = rounding.TryGetProperty("mode", out var modeValue) && modeValue.ValueKind == JsonValueKind.String
            ? modeValue.GetString() switch
            {
                "half_away_from_zero" => MidpointRounding.AwayFromZero,
                "half_to_even" => MidpointRounding.ToEven,
                _ => null,
            }
            : null;
        if (mode is not { } roundingMode)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.rounding.mode", "must be \"half_away_from_zero\" or \"half_to_even\"");
        }

        if (!TryReadInt(rounding, "decimals", out var decimals) || decimals is < 0 or > 3)
        {
            return Invalid<NonSubstantivePolicy>($"{path}.rounding.decimals", "must be a whole number from 0 to 3");
        }

        return Result.Success(new NonSubstantivePolicy(maxShare, decimals, roundingMode));
    }

    private static Result<IReadOnlyList<TierBand>> ReadBands(
        JsonElement element, bool hasTotal, NonSubstantivePolicy? policy, int minimum, int maximum)
    {
        const string path = "$.parameters.tierBands";
        if (!hasTotal)
        {
            return BandsInvalid("tier bands need \"total\": true");
        }

        if (policy is { RoundingDecimals: not 0 })
        {
            return BandsInvalid("tier bands need whole-number totals (rounding decimals 0)");
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            return Invalid<IReadOnlyList<TierBand>>(path, "must be a non-empty list");
        }

        var bands = new List<TierBand>();
        var index = 0;
        foreach (var band in element.EnumerateArray())
        {
            var at = $"{path}[{index}]";
            if (band.ValueKind != JsonValueKind.Object || JsonObjects.FindUnexpectedKey(band, BandKeys) is not null)
            {
                return Invalid<IReadOnlyList<TierBand>>(at, "must be an object with code, min and max");
            }

            if (!band.TryGetProperty("code", out var code)
                || code.ValueKind != JsonValueKind.String
                || code.GetString() is not { } bandCode
                || !Codes.IsSnakeCode(bandCode))
            {
                return Invalid<IReadOnlyList<TierBand>>($"{at}.code", "must be a snake-case code");
            }

            if (!TryReadInt(band, "min", out var min) || !TryReadInt(band, "max", out var max) || min > max)
            {
                return Invalid<IReadOnlyList<TierBand>>(at, "needs whole-number min and max with min <= max");
            }

            bands.Add(new TierBand(bandCode, min, max));
            index++;
        }

        if (bands.Select(band => band.Code).Distinct(StringComparer.Ordinal).Count() != bands.Count)
        {
            return BandsInvalid("band codes must be distinct");
        }

        var ordered = bands.OrderBy(band => band.Min).ToList();
        if (ordered[0].Min != minimum || ordered[^1].Max != maximum)
        {
            return BandsInvalid($"the bands must cover exactly {minimum}–{maximum}, the totals this scope can produce");
        }

        for (var position = 1; position < ordered.Count; position++)
        {
            if (ordered[position].Min != ordered[position - 1].Max + 1)
            {
                return BandsInvalid("the bands must be contiguous, without gaps or overlaps");
            }
        }

        return Result.Success<IReadOnlyList<TierBand>>(ordered);
    }

    private static bool HasString(JsonElement element, string name, string expected) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool TryReadInt(JsonElement element, string name, out int number)
    {
        number = 0;
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }

    private static Error About(Error error, string detail) => error with { Message = $"{error.Message} ({detail})" };

    private static Result<IReadOnlyList<TierBand>> BandsInvalid(string problem) =>
        Result.Failure<IReadOnlyList<TierBand>>(ScoringErrors.TierBandsInvalid with
        {
            Message = $"{ScoringErrors.TierBandsInvalid.Message} Here {problem}.",
        });

    private static Result<T> Invalid<T>(string path, string problem) =>
        Result.Failure<T>(ScoringErrors.DefinitionInvalid with
        {
            Message = $"The scoring definition is invalid at {path}: {problem}.",
        });
}
