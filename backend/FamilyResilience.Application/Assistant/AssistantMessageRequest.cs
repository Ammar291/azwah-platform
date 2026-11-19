namespace FamilyResilience.Application.Assistant;

/// <summary>One message as the app sends it; <c>channel</c> is <c>chat</c> unless it was said on a call. Never stored or logged.</summary>
public sealed record AssistantMessageRequest(AssistantMessageRole Role, string? Text, AssistantMessageChannel? Channel = null)
{
    /// <summary>The trimmed message for a model; call only after validation.</summary>
    public AssistantMessage ToMessage() => new(Role, Text!.Trim(), Channel ?? AssistantMessageChannel.Chat);

    public override string ToString() =>
        $"AssistantMessageRequest {{ Role = {Role}, Channel = {Channel}, TextLength = {Text?.Length ?? 0} }}";
}
