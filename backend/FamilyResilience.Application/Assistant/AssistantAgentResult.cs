namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A finished chat turn: the guarded reply, the screens to offer, the directory entries the reply names, the refreshed
/// memory (or null to keep the old one), how many model steps it took, and the safety check's verdict on the person's
/// message (<see cref="AssistantSafetyVerdict.NotChecked"/> when none ran in time). Sensitive: <see cref="ToString"/>
/// prints counts.
/// </summary>
public sealed record AssistantAgentResult(
    string Reply,
    IReadOnlyList<string> Actions,
    IReadOnlyList<AssistantSourceResponse> Sources,
    string? Memory,
    int Steps,
    AssistantSafetyVerdict Safety)
{
    public override string ToString() =>
        $"AssistantAgentResult {{ ReplyLength = {Reply.Length}, Actions = {Actions.Count}, Sources = {Sources.Count}, HasMemory = {Memory is not null}, Steps = {Steps}, Safety = {Safety} }}";
}
