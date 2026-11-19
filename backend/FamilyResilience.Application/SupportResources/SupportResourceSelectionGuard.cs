using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// Accepts a model reply only when it is exactly <c>{"selected":[handles]}</c> with known, distinct handles inside the
/// limit. Anything else is rejected, so a model can never add text, links or entries that are not in the directory.
/// </summary>
public static partial class SupportResourceSelectionGuard
{
    public const int MaxReplyLength = 4096;

    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 4,
    };

    public static SelectionVerdict Check(
        string? json, IReadOnlyDictionary<string, SupportResource> handles, int limit)
    {
        ArgumentNullException.ThrowIfNull(handles);

        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxReplyLength)
        {
            return Verdict(SelectionVerdictKind.Invalid);
        }

        try
        {
            using var document = JsonDocument.Parse(json, Options);
            return Evaluate(document.RootElement, handles, limit);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            // Malformed JSON is a JsonException; a lone surrogate escape (\uD800) in a name or string makes GetString and
            // Name throw InvalidOperationException; a raw lone surrogate makes Parse throw ArgumentException. Every one
            // is a bad reply, so the whole reply is rejected rather than letting the caller see an exception.
            return Verdict(SelectionVerdictKind.Invalid);
        }
    }

    private static SelectionVerdict Evaluate(
        JsonElement root, IReadOnlyDictionary<string, SupportResource> handles, int limit)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Verdict(SelectionVerdictKind.Invalid);
        }

        var properties = root.EnumerateObject().ToList();
        if (properties.Count != 1
            || !string.Equals(properties[0].Name, "selected", StringComparison.Ordinal)
            || properties[0].Value.ValueKind != JsonValueKind.Array
            || properties[0].Value.GetArrayLength() > limit)
        {
            return Verdict(SelectionVerdictKind.Invalid);
        }

        var names = new List<string>();
        foreach (var item in properties[0].Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return Verdict(SelectionVerdictKind.Invalid);
            }

            var name = item.GetString();
            if (name is null || !HandlePattern().IsMatch(name) || names.Contains(name, StringComparer.Ordinal))
            {
                return Verdict(SelectionVerdictKind.Invalid);
            }

            names.Add(name);
        }

        var selected = new List<SupportResource>(names.Count);
        foreach (var name in names)
        {
            if (!handles.TryGetValue(name, out var resource))
            {
                return Verdict(SelectionVerdictKind.Ungrounded);
            }

            selected.Add(resource);
        }

        return new SelectionVerdict(SelectionVerdictKind.Accepted, selected);
    }

    private static SelectionVerdict Verdict(SelectionVerdictKind kind) => new(kind, []);

    [GeneratedRegex(@"\Ar[0-9]{1,2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex HandlePattern();
}
