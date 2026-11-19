namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A model asking to run a tool: its call ID (to match the output), the tool's name and the arguments as JSON text.
/// The arguments may quote the person: <see cref="ToString"/> never prints them.
/// </summary>
public sealed record AssistantToolCall(string CallId, string Name, string Arguments)
{
    public override string ToString() => $"AssistantToolCall {{ Name = {Name}, ArgumentsLength = {Arguments.Length} }}";
}
