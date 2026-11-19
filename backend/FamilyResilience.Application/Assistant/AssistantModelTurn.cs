namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a model step produced: the reply text, or the tools it wants run (with the items to hand back on the next
/// step), and the tokens it used. Sensitive: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record AssistantModelTurn(
    string? Text,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    IReadOnlyList<AssistantModelOutputItem> Produced,
    AssistantModelUsage Usage)
{
    public static AssistantModelTurn Reply(string text, AssistantModelUsage? usage = null) =>
        new(text, [], [], usage ?? AssistantModelUsage.None);

    public override string ToString() =>
        $"AssistantModelTurn {{ TextLength = {Text?.Length ?? 0}, ToolCalls = {ToolCalls.Count}, Produced = {Produced.Count} }}";
}
