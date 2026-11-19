namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One validated, trimmed message of the conversation, typed in the chat or said on a call. Never logged:
/// <see cref="ToString"/> prints the length only.
/// </summary>
public sealed record AssistantMessage(AssistantMessageRole Role, string Text, AssistantMessageChannel Channel = AssistantMessageChannel.Chat)
{
    public override string ToString() => $"AssistantMessage {{ Role = {Role}, Channel = {Channel}, TextLength = {Text.Length} }}";
}
