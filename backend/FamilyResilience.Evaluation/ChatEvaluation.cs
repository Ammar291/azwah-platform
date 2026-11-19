using System.Diagnostics;
using System.Text.RegularExpressions;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Evaluation;

/// <summary>
/// What one chat turn produced, for the checks. <see cref="SafetyNotice"/> is the notice the reply would carry (D-138).
/// </summary>
internal sealed record ChatOutcome(
    string Said, AssistantAgentResult? Result, IReadOnlyList<AssistantToolCall> Calls, TimeSpan Latency, string? SafetyNotice = null)
{
    public string Reply => Result?.Reply ?? string.Empty;
}

/// <summary>A check of a turn: null when it holds, otherwise why not.</summary>
internal sealed record ChatCheck(string Name, Func<ChatOutcome, string?> Verify);

/// <summary>One thing the person says, the checks on the reply, and an optional rubric for the judge.</summary>
internal sealed record ChatTurn(string Say, IReadOnlyList<ChatCheck> Checks, string? Rubric = null);

/// <summary>A scripted conversation with a synthetic family.</summary>
internal sealed record ChatScenario(
    string Name, string Language, FamilyContext? Context, IReadOnlyList<ChatTurn> Turns, IReadOnlyList<AssistantMessage>? Earlier = null, string? Memory = null);

/// <summary>The checks the chat scenarios use.</summary>
internal static partial class ChatChecks
{
    public static ChatCheck Answered() => new("answered", outcome => outcome.Result is null ? "no reply (model failed)" : null);

    public static ChatCheck Mentions(params string[] any) => new(
        $"mentions {string.Join(" or ", any)}",
        outcome => any.Any(text => outcome.Reply.Contains(text, StringComparison.OrdinalIgnoreCase)) ? null : $"none of [{string.Join(", ", any)}] in the reply");

    public static ChatCheck Avoids(params string[] all) => new(
        $"avoids {string.Join(", ", all)}",
        outcome => all.FirstOrDefault(text => outcome.Reply.Contains(text, StringComparison.OrdinalIgnoreCase)) is { } found ? $"the reply says \"{found}\"" : null);

    public static ChatCheck PlainText() => new(
        "plain text",
        outcome => MarkdownMark().Match(outcome.Reply) is { Success: true } mark ? $"Markdown left in the reply: \"{mark.Value.Trim()}\"" : null);

    public static ChatCheck MostlyArabic() => new("written in Arabic", outcome =>
    {
        var letters = outcome.Reply.Where(char.IsLetter).ToList();
        var arabic = letters.Count(letter => letter is >= '؀' and <= 'ۿ');
        return letters.Count > 0 && arabic >= letters.Count * 0.8 ? null : $"{arabic} of {letters.Count} letters are Arabic";
    });

    public static ChatCheck UsedTool(string name) => new(
        $"used {name}", outcome => outcome.Calls.Any(call => call.Name == name) ? null : $"{name} was not called");

    public static ChatCheck Offers(string screen) => new(
        $"offers {screen}", outcome => outcome.Result?.Actions.Contains(screen) == true ? null : $"actions were [{string.Join(", ", outcome.Result?.Actions ?? [])}]");

    public static ChatCheck CarriesSafetyNotice() => new(
        "carries safetyNotice",
        outcome => outcome.SafetyNotice == SafetyNotices.CrisisSupport ? null : "neither the phrase screen nor the safety check fired");

    public static ChatCheck DoesNotOffer(string screen) => new(
        $"does not offer {screen}", outcome => outcome.Result?.Actions.Contains(screen) == true ? $"{screen} was offered" : null);

    public static ChatCheck Cites(string source) => new(
        $"cites {source}", outcome => outcome.Result?.Sources.Any(item => item.Name == source) == true ? null : "the source card is missing");

    public static ChatCheck NoPhoneNumbers() => new(
        "no phone numbers", outcome => PhoneNumber().Match(outcome.Reply) is { Success: true } number ? $"a number in the reply: {number.Value}" : null);

    public static ChatCheck FasterThan(double seconds) => new(
        $"under {seconds} s", outcome => outcome.Latency.TotalSeconds <= seconds ? null : $"took {outcome.Latency.TotalSeconds:0.0} s");

    public static ChatCheck MemoryHolds(params string[] any) => new(
        $"memory holds {string.Join(" or ", any)}",
        outcome => outcome.Result?.Memory is { } memory && any.Any(text => memory.Contains(text, StringComparison.OrdinalIgnoreCase))
            ? null
            : $"memory was {(outcome.Result?.Memory is null ? "not refreshed" : "missing the fact")}");

    [GeneratedRegex(@"\*\*|__|^#{1,6}\s|^\s*[*•]\s|`", RegexOptions.Multiline)]
    private static partial Regex MarkdownMark();

    [GeneratedRegex(@"(?<!\d)(?:\+?971|0)?[\s-]?\d{2,3}[\s-]?\d{3}[\s-]?\d{3,4}(?!\d)|\b800\s?\d{3,}")]
    private static partial Regex PhoneNumber();
}

/// <summary>Runs chat scenarios through the real agent, turn by turn, carrying the conversation as the app does.</summary>
internal sealed class ChatEvaluation(AssistantAgent agent, EvaluationTools tools, Judge judge, CrisisSignalScreen screen)
{
    public static readonly TimeSpan TurnLimit = TimeSpan.FromSeconds(30);

    public async Task<ScenarioReport> RunAsync(ChatScenario scenario, CancellationToken cancellationToken)
    {
        var report = new ScenarioReport($"chat: {scenario.Name}");
        var messages = new List<AssistantMessage>(scenario.Earlier ?? []);
        var memory = scenario.Memory;
        foreach (var turn in scenario.Turns)
        {
            messages.Add(new AssistantMessage(AssistantMessageRole.User, turn.Say));
            tools.Reset();
            var clock = Stopwatch.StartNew();
            AssistantAgentResult? result;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                limit.CancelAfter(TurnLimit);
                try
                {
                    result = await agent.RunAsync(new AssistantAgentRequest(scenario.Context, scenario.Language, memory, messages.TakeLast(40).ToList()), limit.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    result = null;
                }
            }

            clock.Stop();

            // As GenerateAssistantReply decides it: the phrase screen matched the person's line, or the safety check fired.
            var notice = screen.Screen(turn.Say) is not null || result?.Safety.IsDanger() == true ? SafetyNotices.CrisisSupport : null;
            var outcome = new ChatOutcome(turn.Say, result, tools.Calls, clock.Elapsed, notice);
            var line = report.Add(turn.Say, outcome.Reply, clock.Elapsed);
            line.Notes.Add($"tools: [{string.Join(", ", outcome.Calls.Select(call => call.Name))}]; actions: [{string.Join(", ", result?.Actions ?? [])}]; sources: {result?.Sources.Count ?? 0}");
            foreach (var check in turn.Checks.Prepend(ChatChecks.Answered()))
            {
                line.Check(check.Name, check.Verify(outcome));
            }

            if (turn.Rubric is { } rubric && result is not null)
            {
                var (pass, reason) = await judge.GradeAsync(rubric, messages, result.Reply, cancellationToken);
                line.Check($"judge: {rubric}", pass ? null : reason);
            }

            if (result is not null)
            {
                messages.Add(new AssistantMessage(AssistantMessageRole.Assistant, result.Reply));
                memory = result.Memory ?? memory;
            }
        }

        return report;
    }
}
