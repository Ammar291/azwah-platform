using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The text model behind the Azwah agent (D-119, D-122). <see cref="UnconfiguredAssistantModel"/> is registered until an
/// adapter is: the OpenAI one lives in Infrastructure, reads its settings under <c>Assistant:Model</c> (secrets in user
/// secrets or the deployment's store), is truly asynchronous, honours the token, and logs at most a fixed code: never the
/// request, the context, the conversation or the reply. The model gives guidance only; it never produces a score, band
/// or eligibility decision the platform uses.
/// </summary>
public interface IAssistantModel
{
    /// <summary>Whether a model is configured; false makes the API answer 503 <c>assistant.unavailable</c>.</summary>
    bool IsConfigured { get; }

    /// <summary>One step of the agent loop: the reply, or the tools to run. Exceptions also count as failures.</summary>
    Task<Result<AssistantModelTurn>> NextAsync(AssistantModelStep step, CancellationToken cancellationToken);

    /// <summary>The conversation's new memory as plain text, or a failure (the old memory then stays).</summary>
    Task<Result<string>> SummarizeAsync(AssistantMemoryRequest request, CancellationToken cancellationToken);
}
