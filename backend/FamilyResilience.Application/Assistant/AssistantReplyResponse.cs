namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The assistant's reply (plain text) and when the server finished it; the app screens it may show as buttons
/// (<see cref="AssistantScreens"/> codes); the directory entries the reply names, as the directory holds them; the
/// refreshed memory when one was written (D-122, D-123); and <c>crisis_support</c> when a crisis signal was detected in
/// the person's message, with SOS the first action (D-138). Never logged: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record AssistantReplyResponse(
    string Reply,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> Actions,
    IReadOnlyList<AssistantSourceResponse> Sources,
    string? Memory,
    string? SafetyNotice)
{
    public override string ToString() =>
        $"AssistantReplyResponse {{ ReplyLength = {Reply.Length}, GeneratedAt = {GeneratedAt:O}, Actions = {Actions.Count}, Sources = {Sources.Count}, HasMemory = {Memory is not null}, SafetyNotice = {SafetyNotice} }}";
}
