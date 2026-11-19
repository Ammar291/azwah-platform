namespace FamilyResilience.Application.Assistant;

/// <summary>One piece of a model's input, oldest first. Sensitive: <see cref="ToString"/> prints the length only.</summary>
public sealed record AssistantInput(AssistantInputKind Kind, string Text)
{
    public override string ToString() => $"AssistantInput {{ Kind = {Kind}, TextLength = {Text.Length} }}";
}
