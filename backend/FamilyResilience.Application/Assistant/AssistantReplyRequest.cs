namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The conversation so far, oldest first, ending with the person's new message, the language to answer in, and the
/// memory the server wrote earlier for what no longer fits (D-123). The server keeps none of it: the history lives on
/// the phone. Never logged: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record AssistantReplyRequest(string? Language, IReadOnlyList<AssistantMessageRequest>? Messages, string? Memory = null)
{
    public override string ToString() =>
        $"AssistantReplyRequest {{ Language = {Language}, Messages = {Messages?.Count ?? 0}, HasMemory = {!string.IsNullOrWhiteSpace(Memory)} }}";
}
