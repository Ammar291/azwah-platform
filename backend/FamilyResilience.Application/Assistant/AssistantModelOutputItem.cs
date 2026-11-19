namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Something a model produced in a step (a tool call, or its encrypted reasoning), kept as the adapter's own JSON so it
/// can be sent back exactly. The Application never reads it. Never logged: <see cref="ToString"/> prints the length only.
/// </summary>
public sealed record AssistantModelOutputItem(string Json) : AssistantContinuationItem
{
    public override string ToString() => $"AssistantModelOutputItem {{ Length = {Json.Length} }}";
}
