namespace FamilyResilience.Application.Assistant;

/// <summary>An open live session and the caller's slot. Disposing ends the model session, then frees the slot.</summary>
public sealed class LiveConsultation(IVoiceConsultantSession session, ConsultantSlot slot) : IAsyncDisposable
{
    public IVoiceConsultantSession Session { get; } = session;

    /// <summary>Cancelled when the same subject starts another live session.</summary>
    public CancellationToken Replaced => slot.Replaced;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Session.DisposeAsync();
        }
        finally
        {
            slot.Dispose();
        }
    }
}
