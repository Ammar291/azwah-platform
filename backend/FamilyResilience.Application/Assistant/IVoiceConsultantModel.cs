namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The speech model behind the live Azwah consultant. No adapter ships: <see cref="UnconfiguredVoiceConsultantModel"/>
/// is registered, so a live connection is told <c>unavailable</c> and closed. An adapter for the owner's endpoint lives
/// in Infrastructure under the same rules as <see cref="IAssistantModel"/>: settings in user secrets, truly asynchronous,
/// and nothing logged but a fixed category and an exception type name. Audio and transcripts are never kept.
/// </summary>
public interface IVoiceConsultantModel
{
    /// <summary>Whether a model is configured; false makes a live connection answer <c>unavailable</c>.</summary>
    bool IsConfigured { get; }

    /// <summary>Opens a live session with the context and language. A fault or time-out ends the start with <c>model_failed</c>.</summary>
    Task<IVoiceConsultantSession> OpenAsync(VoiceConsultantRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a live session once <paramref name="request"/> is ready (D-141): an adapter may connect while the request is
    /// still being prepared (the family context built, the earlier conversation screened), so the preparation adds no
    /// wait of its own. By default it waits for the request, then opens. A request that fails to be prepared fails the open.
    /// </summary>
    async Task<IVoiceConsultantSession> OpenAsync(Task<VoiceConsultantRequest> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await OpenAsync(await request.WaitAsync(cancellationToken), cancellationToken);
    }
}
