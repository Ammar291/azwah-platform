using System.Text.Json;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Something the Azwah agent may do (D-122). A tool runs on the server for the signed-in caller, reads only what the
/// caller may read, trusts nothing about identity from its arguments, and returns data, never instructions. It must
/// not throw for bad arguments (answer <see cref="AssistantToolResult.Problem"/> instead) and logs nothing.
/// </summary>
public interface IAssistantTool
{
    AssistantToolDefinition Definition { get; }

    Task<AssistantToolResult> InvokeAsync(JsonElement arguments, string language, CancellationToken cancellationToken);
}
