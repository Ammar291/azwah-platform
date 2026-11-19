using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Loads and validates the crisis phrase list (<c>crisis-signals.v1.json</c>, an embedded resource). Each rule has a code,
/// a concern, <c>phrases</c> by language and optionally <c>except</c> by language (<see cref="CrisisSignalRule.Exceptions"/>).
/// An invalid list throws, so a bad list fails start-up instead of silently screening nothing.
/// </summary>
public static partial class CrisisSignalCatalog
{
    public const string SupportedVersion = "crisis-signals.v1";
    public const string ResourceName = "crisis-signals.v1.json";

    private const string Invalid = "crisis_signal_list_invalid: ";

    public static CrisisSignalList Load()
    {
        using var stream = typeof(CrisisSignalCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(Invalid + "resource_missing");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static CrisisSignalList Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException
            || (ex is InvalidOperationException && !ex.Message.StartsWith(Invalid, StringComparison.Ordinal)))
        {
            // Malformed JSON, or a value of the wrong kind (System.Text.Json throws InvalidOperationException).
            throw new InvalidOperationException(Invalid + "malformed", ex);
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    private static CrisisSignalList Read(JsonElement root)
    {
        var version = Property(root, "version").GetString();
        if (version != SupportedVersion)
        {
            throw Fail("unsupported_version");
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var allPhrases = new HashSet<string>(StringComparer.Ordinal);
        var rules = new List<CrisisSignalRule>();
        foreach (var element in Property(root, "rules").EnumerateArray())
        {
            var code = Property(element, "code").GetString() ?? string.Empty;
            if (code.Length > SafetyAlert.MaxSignalCodeLength || !CodePattern().IsMatch(code))
            {
                throw Fail("invalid_rule_code");
            }

            if (!codes.Add(code))
            {
                throw Fail("duplicate_rule_code");
            }

            var concern = ParseConcern(Property(element, "concern").GetString());
            var phrases = ReadPhrases(Property(element, "phrases"));
            if (phrases.Count == 0)
            {
                throw Fail("rule_without_phrases");
            }

            if (phrases.Any(phrase => !allPhrases.Add(phrase)))
            {
                throw Fail("duplicate_phrase");
            }

            // Optional: phrases that cancel an occurrence of this rule's phrases they share a word with ("hits me up").
            var exceptions = element.TryGetProperty("except", out var except) ? ReadPhrases(except) : [];
            rules.Add(new CrisisSignalRule(code, concern, phrases) { Exceptions = exceptions });
        }

        return new CrisisSignalList(version, rules);
    }

    /// <summary>The normalised phrases of an object of <c>en</c> and <c>ar</c> lists.</summary>
    private static List<string> ReadPhrases(JsonElement byLanguage)
    {
        var phrases = new List<string>();
        foreach (var language in byLanguage.EnumerateObject())
        {
            if (language.Name is not ("en" or "ar"))
            {
                throw Fail("unknown_language");
            }

            foreach (var raw in language.Value.EnumerateArray())
            {
                var phrase = CrisisTextNormalizer.Normalize(raw.GetString());
                if (phrase.Length == 0)
                {
                    throw Fail("empty_phrase");
                }

                phrases.Add(phrase);
            }
        }

        return phrases;
    }

    private static SafetyConcern ParseConcern(string? code) => code switch
    {
        "suicide_or_self_harm" => SafetyConcern.SuicideOrSelfHarm,
        "harm_to_others" => SafetyConcern.HarmToOthers,
        "abuse_or_danger" => SafetyConcern.AbuseOrDanger,
        _ => throw Fail("unknown_concern"),
    };

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : throw Fail("malformed");

    private static InvalidOperationException Fail(string reason) => new(Invalid + reason);
}
