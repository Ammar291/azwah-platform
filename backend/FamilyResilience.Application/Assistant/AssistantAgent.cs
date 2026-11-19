namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The Azwah agent's loop for one chat turn (D-122), an explicit state graph:
/// <list type="number">
/// <item><b>prepare</b>: the instructions and input; the safety check of the person's message and, for a long
/// conversation, the memory refresh start alongside, so neither adds a wait.</item>
/// <item><b>model</b>: one step with the tools; at most <see cref="MaxSteps"/>, the last without tools so it must answer.</item>
/// <item><b>tools</b>: each requested call through <see cref="IAssistantToolRunner"/>, its output handed back as data.</item>
/// <item><b>guard</b>: plain text, tidy and capped (<see cref="AssistantReplyGuard"/>).</item>
/// <item><b>finish</b>: the screens the tools offered (SOS first, added whatever the model said when the safety check
/// flags danger), the directory entries the reply names, the new memory, and the safety verdict, which the use case
/// records for staff (D-136).</item>
/// </list>
/// A failed or empty model step ends the turn as null (the use case answers 502). Exceptions propagate to the use case,
/// which treats them as a failure; the side tasks never throw.
/// </summary>
public sealed class AssistantAgent(IAssistantModel model, IAssistantToolRunner tools, IAssistantSafetyCheck safety, AssistantMetrics metrics)
{
    public const int MaxSteps = 4;

    /// <summary>How long the finished reply waits for a safety check or a memory refresh still running.</summary>
    public static readonly TimeSpan SideTaskGrace = TimeSpan.FromSeconds(2);

    public async Task<AssistantAgentResult?> RunAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var sideTasks = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var safetyCheck = CheckSafetyAsync(request.Messages[^1].Text, sideTasks.Token);
            var memoryRefresh = request.Messages.Count >= AssistantMemory.RefreshAt
                ? RefreshMemoryAsync(request, sideTasks.Token)
                : Task.FromResult<string?>(null);

            var instructions = AssistantPrompt.ChatInstructions(request.Language);
            var input = AssistantPrompt.ChatInput(request.Context, request.Memory, request.Messages);
            var continuation = new List<AssistantContinuationItem>();
            var actions = new List<string>();
            var found = new List<AssistantSourceResponse>();
            string? text = null;
            var steps = 0;
            while (text is null && steps < MaxSteps)
            {
                steps++;
                var toolsAllowed = steps < MaxSteps;
                var turn = await model.NextAsync(
                    new AssistantModelStep(instructions, input, tools.Definitions, continuation.ToList(), toolsAllowed), cancellationToken);
                metrics.ModelCalled("reply", turn.IsSuccess, turn.IsSuccess ? turn.Value.Usage : null);
                if (turn.IsFailure)
                {
                    return null;
                }

                if (turn.Value.ToolCalls.Count > 0 && toolsAllowed)
                {
                    continuation.AddRange(turn.Value.Produced);
                    foreach (var call in turn.Value.ToolCalls)
                    {
                        var result = await tools.RunAsync(call, request.Language, cancellationToken);
                        continuation.Add(new AssistantToolOutputItem(call.CallId, result.Output));
                        actions.AddRange(result.Actions);
                        found.AddRange(result.Sources);
                    }

                    continue;
                }

                text = turn.Value.Text ?? string.Empty;
            }

            var reply = AssistantReplyGuard.Clean(text ?? string.Empty);
            if (reply.Length == 0)
            {
                return null;
            }

            var verdict = await WithinGraceAsync(safetyCheck, AssistantSafetyVerdict.NotChecked);
            if (verdict.IsDanger())
            {
                actions.Insert(0, AssistantScreens.Sos);
                metrics.SafetyFlagged("chat");
            }

            var memory = await WithinGraceAsync(memoryRefresh, null);
            return new AssistantAgentResult(reply, AssistantScreens.Order(actions), Named(reply, found), memory, steps, verdict);
        }
        finally
        {
            await sideTasks.CancelAsync();
        }
    }

    /// <summary>The directory entries the reply names (the tool found them; the reply mentions them), at most three.</summary>
    internal static IReadOnlyList<AssistantSourceResponse> Named(string reply, IEnumerable<AssistantSourceResponse> found)
    {
        var text = Fold(reply);
        return found
            .DistinctBy(source => source.Id)
            .Where(source => Fold(source.Name) is { Length: > 2 } name && text.Contains(name, StringComparison.Ordinal))
            .Take(3)
            .ToList();
    }

    private static string Fold(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private async Task<AssistantSafetyVerdict> CheckSafetyAsync(string text, CancellationToken cancellationToken)
    {
        if (!safety.IsConfigured)
        {
            return AssistantSafetyVerdict.NotChecked;
        }

        try
        {
            var verdict = await safety.CheckAsync(text, cancellationToken);
            metrics.ModelCalled("safety", verdict != AssistantSafetyVerdict.NotChecked);
            return verdict;
        }
        catch (Exception)
        {
            // Never fails the turn: without a verdict the prompt's own SOS rule still applies.
            return AssistantSafetyVerdict.NotChecked;
        }
    }

    private async Task<string?> RefreshMemoryAsync(AssistantAgentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var older = request.Messages.Take(request.Messages.Count - AssistantMemory.KeepRecent).ToList();
            var summary = await model.SummarizeAsync(
                new AssistantMemoryRequest(AssistantPrompt.MemoryInstructions(request.Language), AssistantMemory.Normalise(request.Memory), older),
                cancellationToken);
            metrics.ModelCalled("memory", summary.IsSuccess);
            return summary.IsSuccess ? AssistantMemory.FromModel(summary.Value) : null;
        }
        catch (Exception)
        {
            // The old memory stays; the next long request tries again.
            return null;
        }
    }

    private static async Task<T> WithinGraceAsync<T>(Task<T> task, T fallback)
    {
        if (task.IsCompleted)
        {
            return await task;
        }

        using var timer = new CancellationTokenSource();
        var finished = await Task.WhenAny(task, Task.Delay(SideTaskGrace, timer.Token));
        await timer.CancelAsync();
        return finished == task ? await task : fallback;
    }
}
