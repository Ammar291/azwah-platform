using System.Text.Json;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The agent's allow-listed tools (D-122), scoped to the request. Calls run one at a time, because tools share the
/// request's database context and a live call may ask for several at once. An unknown tool, arguments that are not a
/// JSON object, or a tool that throws each answer a fixed problem code; nothing is logged.
/// </summary>
public sealed class AssistantToolbox : IAssistantToolRunner, IDisposable
{
    private readonly IReadOnlyList<IAssistantTool> _tools;
    private readonly AssistantMetrics _metrics;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AssistantToolbox(IEnumerable<IAssistantTool> tools, AssistantMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools.ToList();
        _metrics = metrics;
        Definitions = _tools.Select(tool => tool.Definition).ToList();
    }

    public IReadOnlyList<AssistantToolDefinition> Definitions { get; }

    public async Task<AssistantToolResult> RunAsync(AssistantToolCall call, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var tool = _tools.FirstOrDefault(candidate => string.Equals(candidate.Definition.Name, call.Name, StringComparison.Ordinal));
        if (tool is null)
        {
            _metrics.ToolCalled("unknown", "unknown_tool");
            return AssistantToolResult.Problem("unknown_tool");
        }

        var result = await InvokeAsync(tool, call.Arguments, language, cancellationToken);
        _metrics.ToolCalled(tool.Definition.Name, result.Outcome);
        return result;
    }

    public void Dispose() => _gate.Dispose();

    private async Task<AssistantToolResult> InvokeAsync(IAssistantTool tool, string arguments, string language, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
        }
        catch (JsonException)
        {
            return AssistantToolResult.Problem("invalid");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return AssistantToolResult.Problem("invalid");
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                return await tool.InvokeAsync(document.RootElement, language, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Broad on purpose: a tool fault is told to the model as a code; the exception could quote the person.
                return AssistantToolResult.Problem("failed");
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
