namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One chat turn for the agent: the caller's family context (null without a household), the language, the memory and
/// the conversation, oldest first, ending with the person's message. Sensitive: <see cref="ToString"/> prints counts.
/// </summary>
public sealed record AssistantAgentRequest(FamilyContext? Context, string Language, string? Memory, IReadOnlyList<AssistantMessage> Messages)
{
    public override string ToString() =>
        $"AssistantAgentRequest {{ HasContext = {Context is not null}, Language = {Language}, HasMemory = {Memory is not null}, Messages = {Messages.Count} }}";
}
