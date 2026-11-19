namespace FamilyResilience.Application.Assistant;

/// <summary>The default: no voice model is configured, so no session ever opens and nothing is sent anywhere.</summary>
public sealed class UnconfiguredVoiceConsultantModel : IVoiceConsultantModel
{
    public bool IsConfigured => false;

    public Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken) =>
        Task.FromException<IVoiceConsultantSession>(new InvalidOperationException("No voice consultant model is configured."));
}
