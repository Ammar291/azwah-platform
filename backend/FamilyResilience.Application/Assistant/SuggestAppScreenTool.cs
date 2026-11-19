using System.Text.Json;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// <c>suggest_app_screen</c>: offers the person a button for a place in the app (D-122). The app maps the code to its
/// own route and translated label; nothing navigates by itself and nothing is read.
/// </summary>
public sealed class SuggestAppScreenTool : IAssistantTool
{
    public const string Name = "suggest_app_screen";

    private static readonly string Schema =
        """{"type":"object","properties":{"screen":{"type":"string","enum":["""
        + string.Join(',', AssistantScreens.All.Select(code => $"\"{code}\""))
        + """],"description":"The place to offer."}},"required":["screen"],"additionalProperties":false}""";

    /// <summary>The tool as a model sees it; the same for every caller.</summary>
    public static AssistantToolDefinition ToolDefinition { get; } = new(
        Name,
        "Show the person a button that opens a place in the Azwah app. sos: immediate help and emergency numbers, use it at "
        + "once if anyone may be in danger. services: All services, the approved support directory and emergency numbers. "
        + "home: the family's check-ins and questionnaires. resilience: the family's resilience results. help: help with "
        + "the app. consultant: talk it through by voice with the Azwah consultant. assistant: this chat.",
        Schema);

    public AssistantToolDefinition Definition => ToolDefinition;

    public Task<AssistantToolResult> InvokeAsync(JsonElement arguments, string language, CancellationToken cancellationToken)
    {
        var screen = arguments.TryGetProperty("screen", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return Task.FromResult(AssistantScreens.IsKnown(screen)
            ? new AssistantToolResult("""{"shown":true}""", "ok", [screen!], [])
            : AssistantToolResult.Problem("invalid"));
    }
}
