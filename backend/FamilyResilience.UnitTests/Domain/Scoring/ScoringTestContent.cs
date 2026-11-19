using System.Text.Json;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.UnitTests.Domain.Scoring;

/// <summary>Synthetic scoring content. Not the codebook, and no real scoring rule.</summary>
internal static class ScoringTestContent
{
    public const string Minimal = """{"reverseScoring":"mirror_scale","unanswered":"reject","total":true}""";

    public static ScoringItem Item(
        string code,
        ScoringDirection direction = ScoringDirection.Forward,
        int scaleSize = 5,
        bool nonSubstantive = false,
        string? category = null) =>
        ItemWithValues(code, Enumerable.Range(1, scaleSize), direction, nonSubstantive, category);

    public static ScoringItem ItemWithValues(
        string code,
        IEnumerable<int> values,
        ScoringDirection direction = ScoringDirection.Forward,
        bool nonSubstantive = false,
        string? category = null)
    {
        var options = values.Select(value => new ScoringOption(ResponseOptionId.New(), value, false)).ToList();
        if (nonSubstantive)
        {
            options.Add(new ScoringOption(ResponseOptionId.New(), 99, true));
        }

        return new ScoringItem(InstrumentItemId.New(), code, category, direction, options);
    }

    public static ScoringContent Content(params ScoringItem[] items) => new(items);

    /// <summary><paramref name="itemCount"/> forward items on a 1..<paramref name="scaleSize"/> scale, optionally offering 99.</summary>
    public static ScoringContent Module(int itemCount, int scaleSize = 5, bool nonSubstantive = false) =>
        new(Enumerable.Range(1, itemCount)
            .Select(number => Item($"Q{number:00}", scaleSize: scaleSize, nonSubstantive: nonSubstantive))
            .ToList());

    public static ScoringResponse Answer(ScoringItem item, int value)
    {
        var option = item.Options.Single(candidate => candidate.Value == value);
        return new ScoringResponse(item.ItemId, option.OptionId, value);
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Parameters for items that offer 99, excluding them up to <paramref name="maxShare"/>; <paramref name="extra"/> is appended inside the object.</summary>
    public static string WithNonSubstantive(string maxShare = "0.3", string mode = "half_away_from_zero", int decimals = 0, string extra = "")
    {
        var rounding = $$"""{"mode":"{{mode}}","decimals":{{decimals}}}""";
        return $$"""{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"nonSubstantive":{"maxShare":{{maxShare}},"aggregation":"prorated_mean","rounding":{{rounding}}}{{extra}}}""";
    }

    /// <summary>A <c>tierBands</c> member to append with <paramref name="extra"/>-style concatenation.</summary>
    public static string Bands(params (string Code, int Min, int Max)[] bands) =>
        ",\"tierBands\":[" + string.Join(",", bands.Select(band => $$"""{"code":"{{band.Code}}","min":{{band.Min}},"max":{{band.Max}}}""")) + "]";
}
