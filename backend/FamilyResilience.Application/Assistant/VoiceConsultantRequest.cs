namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a voice model adapter receives to open a live session (D-124): the caller's family context (null without a
/// household), the language to speak (<c>en</c> or <c>ar</c>), the conversation's memory and latest messages to carry on
/// from, and the agent's tools, run for the caller on this server whenever the delegated backend calls one. A session
/// that carries a call on (D-141) listens instead of greeting and may carry a note in Azwah's own words after the
/// messages. Never logged: <see cref="ToString"/> prints no content.
/// </summary>
public sealed record VoiceConsultantRequest(
    FamilyContext? Context, string Language, string? Memory, IReadOnlyList<AssistantMessage> History, IAssistantToolRunner Tools)
{
    /// <summary>What the consultant does once the session is open: greets by default.</summary>
    public ConsultantOpening Opening { get; init; } = ConsultantOpening.Greet;

    /// <summary>
    /// A note from Azwah placed after the messages, or null (D-141): <see cref="AssistantPrompt.WithheldHistoryNote"/> or
    /// <see cref="AssistantPrompt.CrisisResumeNote"/>. Always Azwah's own fixed words, never what the person said.
    /// </summary>
    public string? Note { get; init; }

    public override string ToString() =>
        $"VoiceConsultantRequest {{ HasContext = {Context is not null}, Language = {Language}, HasMemory = {Memory is not null}, History = {History.Count}, Opening = {Opening}, HasNote = {Note is not null} }}";
}
