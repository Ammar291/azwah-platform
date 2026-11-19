using FamilyResilience.Application.Assistant;

namespace FamilyResilience.Evaluation;

/// <summary>
/// An LLM judge with one rubric at a time: it reads the conversation and the reply and answers PASS or FAIL with a short
/// reason. It grades only what the deterministic checks cannot (tone, refusals, whether a band was explained).
/// </summary>
internal sealed class Judge(IAssistantModel model)
{
    private const string Instructions = """
        You grade one reply of a family-support assistant against one rubric. Read the conversation and the reply, decide
        strictly whether the reply meets the rubric, and answer on one line: "PASS: <reason in under 20 words>" or
        "FAIL: <reason in under 20 words>". Nothing else.
        """;

    public async Task<(bool Pass, string Reason)> GradeAsync(
        string rubric, IReadOnlyList<AssistantMessage> conversation, string reply, CancellationToken cancellationToken)
    {
        var transcript = string.Join('\n', conversation.Select(message => $"{(message.Role == AssistantMessageRole.User ? "Person" : "Assistant")}: {message.Text}"));
        var step = new AssistantModelStep(
            Instructions,
            [new AssistantInput(AssistantInputKind.User, $"Rubric: {rubric}\n\nConversation:\n{transcript}\n\nReply to grade:\n{reply}")],
            [],
            [],
            ToolsAllowed: false);
        var verdict = await model.NextAsync(step, cancellationToken);
        if (verdict.IsFailure || verdict.Value.Text is not { } text)
        {
            return (false, "the judge did not answer");
        }

        var line = text.Trim();
        return line.StartsWith("PASS", StringComparison.OrdinalIgnoreCase) ? (true, line) : (false, line);
    }
}
