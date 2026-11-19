namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a model summarises into the conversation's new memory (D-123): the instructions, the old memory and the
/// messages leaving the window. Sensitive: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record AssistantMemoryRequest(string Instructions, string? PreviousMemory, IReadOnlyList<AssistantMessage> Messages)
{
    public override string ToString() =>
        $"AssistantMemoryRequest {{ HasPreviousMemory = {PreviousMemory is not null}, Messages = {Messages.Count} }}";
}
