namespace FamilyResilience.Application.Assistant;

/// <summary>The default: no check is configured, so nothing is sent anywhere; the prompt's SOS rule still applies.</summary>
public sealed class NoAssistantSafetyCheck : IAssistantSafetyCheck
{
    public bool IsConfigured => false;

    public Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult(AssistantSafetyVerdict.NotChecked);
}
