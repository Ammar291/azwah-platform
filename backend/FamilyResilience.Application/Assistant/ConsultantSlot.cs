namespace FamilyResilience.Application.Assistant;

/// <summary>A subject's claim on their one live session. Disposing it frees the slot unless a newer one replaced it.</summary>
public sealed class ConsultantSlot : IDisposable
{
    private readonly LiveConsultantSessions _owner;
    private readonly CancellationTokenSource _replaced = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    internal ConsultantSlot(LiveConsultantSessions owner, string subject)
    {
        _owner = owner;
        Subject = subject;
    }

    /// <summary>Cancelled when the same subject starts another live session.</summary>
    public CancellationToken Replaced => _replaced.Token;

    internal string Subject { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Release(this);
            _replaced.Dispose();
        }
    }

    internal void Replace()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _replaced.Cancel();
            }
        }
    }
}
