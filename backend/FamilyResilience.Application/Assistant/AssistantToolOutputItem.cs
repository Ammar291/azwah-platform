namespace FamilyResilience.Application.Assistant;

/// <summary>A tool's output for the call it answers. Never logged: <see cref="ToString"/> prints the length only.</summary>
public sealed record AssistantToolOutputItem(string CallId, string Output) : AssistantContinuationItem
{
    public override string ToString() => $"AssistantToolOutputItem {{ OutputLength = {Output.Length} }}";
}
