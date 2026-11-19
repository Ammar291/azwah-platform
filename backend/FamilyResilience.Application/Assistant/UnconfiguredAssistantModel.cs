using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assistant;

/// <summary>The default: no model is configured, so nothing is ever sent anywhere and the API answers 503.</summary>
public sealed class UnconfiguredAssistantModel : IAssistantModel
{
    public bool IsConfigured => false;

    public Task<Result<AssistantModelTurn>> NextAsync(AssistantModelStep step, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<AssistantModelTurn>(AssistantErrors.Unavailable));

    public Task<Result<string>> SummarizeAsync(AssistantMemoryRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<string>(AssistantErrors.Unavailable));
}
