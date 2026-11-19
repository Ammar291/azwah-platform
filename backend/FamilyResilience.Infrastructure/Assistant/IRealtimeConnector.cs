namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>Opens <see cref="IRealtimeConnection"/>s to OpenAI's Realtime API with the key in the connection's header.</summary>
public interface IRealtimeConnector
{
    Task<IRealtimeConnection> ConnectAsync(Uri address, string apiKey, CancellationToken cancellationToken);
}
