using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// <c>search_support_directory</c>: the approved support directory, through the same deterministic search as the app
/// (D-93, <see cref="SearchSupportResources.SearchAsync"/>; visibility from the token's roles). It returns at most five
/// entries in the person's language, with name, provider, summary and link, marked information only. The search text is
/// the model's words for the need, not the person's, so it is not crisis-screened (the chat screens the person's own
/// message); like any search text it is never stored, logged or audited.
/// </summary>
public sealed partial class SearchSupportDirectoryTool(SearchSupportResources search) : IAssistantTool
{
    public const string Name = "search_support_directory";

    private const int MaxTextLength = 200;
    private const int Limit = 5;

    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The tool as a model sees it; the same for every caller.</summary>
    public static AssistantToolDefinition ToolDefinition { get; } = new(
        Name,
        "Search Azwah's approved directory of public support services in the United Arab Emirates (for example family "
        + "counselling, parenting, household budget and debt advice). Use it whenever the person asks where to get help, "
        + "for a service, an organisation or a programme. Returns up to 5 entries with name, provider, summary and link, or "
        + "none. Entries are information only, not referrals.",
        """
        {"type":"object","properties":{
        "text":{"type":"string","description":"The need in a few words, in the person's language, for example: debt advice."},
        "category":{"type":["string","null"],"description":"An optional snake_case category code, for example financial, counselling or parenting; null when unsure."}},
        "required":["text","category"],"additionalProperties":false}
        """);

    public AssistantToolDefinition Definition => ToolDefinition;

    public async Task<AssistantToolResult> InvokeAsync(JsonElement arguments, string language, CancellationToken cancellationToken)
    {
        var text = Clip(StringArgument(arguments, "text"));
        var category = StringArgument(arguments, "category")?.Trim();
        if (category is not null && !CategoryCode().IsMatch(category))
        {
            category = null;
        }

        if (text is null && category is null)
        {
            return AssistantToolResult.Problem("invalid");
        }

        var found = await search.SearchAsync(
            new SearchSupportResourcesRequest(text, category is null ? null : [category], null, Limit), cancellationToken);
        if (found.IsFailure)
        {
            return AssistantToolResult.Problem("failed");
        }

        var arabic = language == AssistantLanguages.Arabic;
        var entries = found.Value.Matches.Select(match => new
        {
            match.Id,
            Name = Pick(arabic, match.NameAr, match.NameEn),
            Provider = Pick(arabic, match.ProviderNameAr, match.ProviderNameEn),
            Summary = Pick(arabic, match.SummaryAr, match.SummaryEn),
            match.Url,
        }).ToList();
        var output = JsonSerializer.Serialize(
            new
            {
                notice = "information_only",
                matches = entries.Select(entry => new { name = entry.Name, provider = entry.Provider, summary = entry.Summary, url = entry.Url }),
                note = entries.Count == 0 ? "No approved entry matched. Offer the services screen." : null,
            },
            OutputOptions);
        return new AssistantToolResult(
            output,
            entries.Count == 0 ? "no_match" : "ok",
            [],
            entries.Select(entry => new AssistantSourceResponse(entry.Id, entry.Name, entry.Provider, entry.Url)).ToList());
    }

    private static string? StringArgument(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The text without control characters, at most 200 characters, or null when nothing is left.</summary>
    private static string? Clip(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var cleaned = new string(text.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length > MaxTextLength)
        {
            cleaned = cleaned[..MaxTextLength].Trim();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string Pick(bool arabic, string arabicText, string englishText) =>
        arabic && !string.IsNullOrWhiteSpace(arabicText) ? arabicText : englishText;

    [GeneratedRegex("^[a-z][a-z0-9_]{1,63}$")]
    private static partial Regex CategoryCode();
}
