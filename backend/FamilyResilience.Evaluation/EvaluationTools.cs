using System.Text.Json;
using FamilyResilience.Application.Assistant;

namespace FamilyResilience.Evaluation;

/// <summary>
/// The agent's tools for the evaluation: the real screen tool, and the directory tool with the same definition over a
/// synthetic directory (no database). Every call is recorded so scenarios can check what the agent did.
/// </summary>
internal sealed class EvaluationTools(AssistantMetrics metrics) : IAssistantToolRunner
{
    public const string DebtEntry = "Household budget and debt advice";

    public const string DebtEntryArabic = "استشارات ميزانية الأسرة والديون";

    private readonly AssistantToolbox _toolbox = new([new SyntheticDirectoryTool(), new SuggestAppScreenTool()], metrics);
    private readonly List<AssistantToolCall> _calls = [];

    public IReadOnlyList<AssistantToolDefinition> Definitions => _toolbox.Definitions;

    public IReadOnlyList<AssistantToolCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public void Reset()
    {
        lock (_calls)
        {
            _calls.Clear();
        }
    }

    public Task<AssistantToolResult> RunAsync(AssistantToolCall call, string language, CancellationToken cancellationToken)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }

        return _toolbox.RunAsync(call, language, cancellationToken);
    }

    /// <summary>The directory tool's definition over three synthetic entries; a search matches on a few keywords.</summary>
    private sealed class SyntheticDirectoryTool : IAssistantTool
    {
        /// <summary>Synthetic test entries (not real services), in both languages as the approved directory holds them.</summary>
        private static readonly (string[] Keys, Guid Id, string Url, (string Name, string Provider, string Summary) English, (string Name, string Provider, string Summary) Arabic)[] Entries =
        [
            (["debt", "budget", "money", "loan", "financ", "دين", "ديون", "مال", "ميزانية"], Guid.Parse("0199e0a1-0000-7000-9000-00000000e001"), "https://example.org/azwah-eval/debt",
                (DebtEntry, "Sample provider B", "Help planning a household budget and handling debts."),
                (DebtEntryArabic, "الجهة التجريبية ب", "مساعدة في تخطيط ميزانية الأسرة والتعامل مع الديون.")),
            (["counsel", "argu", "conflict", "fight", "marri", "إرشاد", "خلاف", "مشاكل"], Guid.Parse("0199e0a1-0000-7000-9000-00000000e002"), "https://example.org/azwah-eval/counselling",
                ("Family counselling line", "Sample provider A", "Talk through family worries with a counsellor."),
                ("خط الإرشاد الأسري", "الجهة التجريبية أ", "تحدّث مع مرشد عن هموم الأسرة.")),
            (["parent", "teen", "child", "screen", "phone", "تربية", "أبناء", "مراهق", "هاتف"], Guid.Parse("0199e0a1-0000-7000-9000-00000000e003"), "https://example.org/azwah-eval/parenting",
                ("Parenting teenagers course", "Sample provider C", "Short courses for parents of teenagers."),
                ("دورة تربية المراهقين", "الجهة التجريبية ج", "دورات قصيرة لآباء وأمهات المراهقين.")),
        ];

        public AssistantToolDefinition Definition => SearchSupportDirectoryTool.ToolDefinition;

        public Task<AssistantToolResult> InvokeAsync(JsonElement arguments, string language, CancellationToken cancellationToken)
        {
            var text = (arguments.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null) ?? string.Empty;
            var category = arguments.TryGetProperty("category", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
            var query = $"{text} {category}".ToLowerInvariant();
            var arabic = language == AssistantLanguages.Arabic;
            var matches = Entries
                .Where(entry => entry.Keys.Any(key => query.Contains(key, StringComparison.Ordinal)))
                .Select(entry => (entry.Id, entry.Url, Text: arabic ? entry.Arabic : entry.English))
                .ToList();
            var output = JsonSerializer.Serialize(new
            {
                notice = "information_only",
                matches = matches.Select(match => new { name = match.Text.Name, provider = match.Text.Provider, summary = match.Text.Summary, url = match.Url }),
                note = matches.Count == 0 ? "No approved entry matched. Offer the services screen." : null,
            });
            return Task.FromResult(new AssistantToolResult(
                output,
                matches.Count == 0 ? "no_match" : "ok",
                [],
                matches.Select(match => new AssistantSourceResponse(match.Id, match.Text.Name, match.Text.Provider, match.Url)).ToList()));
        }
    }
}
