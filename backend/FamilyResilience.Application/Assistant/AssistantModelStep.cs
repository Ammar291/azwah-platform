namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One step of the agent loop for a model adapter (D-122): the instructions, the input (data notes and the
/// conversation, oldest first), the tools it may call, what earlier steps produced and the tools answered, and whether
/// it may call tools now (false on the last step, so it must answer). Sensitive: <see cref="ToString"/> prints counts.
/// </summary>
public sealed record AssistantModelStep(
    string Instructions,
    IReadOnlyList<AssistantInput> Input,
    IReadOnlyList<AssistantToolDefinition> Tools,
    IReadOnlyList<AssistantContinuationItem> Continuation,
    bool ToolsAllowed)
{
    public override string ToString() =>
        $"AssistantModelStep {{ Input = {Input.Count}, Tools = {Tools.Count}, Continuation = {Continuation.Count}, ToolsAllowed = {ToolsAllowed} }}";
}
