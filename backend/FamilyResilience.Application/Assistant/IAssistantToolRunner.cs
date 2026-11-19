namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Runs the agent's tools for the signed-in caller (D-122): the chat's loop and a live call's delegated backend both go
/// through it. It answers every call, including unknown tools and bad arguments, with data the model can read, and
/// throws only for the caller's own cancellation.
/// </summary>
public interface IAssistantToolRunner
{
    IReadOnlyList<AssistantToolDefinition> Definitions { get; }

    Task<AssistantToolResult> RunAsync(AssistantToolCall call, string language, CancellationToken cancellationToken);
}
