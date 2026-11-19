namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The app's first message on a live call, protocol version 2 (D-123, D-124): the conversation's memory and the latest
/// messages of the chat and earlier calls, so the consultant carries on from them. The server keeps none of it. Never
/// logged: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ConsultantStartMessage(string? Type, string? Memory, IReadOnlyList<AssistantMessageRequest>? Messages)
{
    public const string TypeName = "start";

    public override string ToString() =>
        $"ConsultantStartMessage {{ Type = {Type}, HasMemory = {!string.IsNullOrWhiteSpace(Memory)}, Messages = {Messages?.Count ?? 0} }}";
}
