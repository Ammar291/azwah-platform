namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a tool gives back: the output the model reads (JSON data), a fixed outcome code for the metrics (<c>ok</c>,
/// <c>no_match</c>, <c>invalid</c>, <c>unknown_tool</c>, <c>failed</c>), the app screens it offers, and the directory
/// entries it found. Never logged: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record AssistantToolResult(
    string Output, string Outcome, IReadOnlyList<string> Actions, IReadOnlyList<AssistantSourceResponse> Sources)
{
    public static AssistantToolResult Problem(string outcome) =>
        new($$"""{"error":"{{outcome}}"}""", outcome, [], []);

    public override string ToString() =>
        $"AssistantToolResult {{ Outcome = {Outcome}, OutputLength = {Output.Length}, Actions = {Actions.Count}, Sources = {Sources.Count} }}";
}
