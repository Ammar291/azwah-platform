namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// One WebSocket to OpenAI's Realtime API, carrying JSON events both ways (D-120). A seam so the session's logic is tested
/// without a network; <see cref="WebSocketRealtimeConnector"/> is the real one. Sends may come from two callers at once,
/// so an implementation serialises them; one reader receives at a time. Disposing closes the socket.
/// </summary>
public interface IRealtimeConnection : IAsyncDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken);

    /// <summary>The next whole event, or null once OpenAI closed the socket normally. Any other ending throws.</summary>
    Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken);
}
