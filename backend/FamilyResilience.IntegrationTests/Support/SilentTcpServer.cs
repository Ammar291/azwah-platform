using System.Net;
using System.Net.Sockets;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// A local TCP endpoint that accepts connections and never answers, like a database host that has stopped responding.
/// </summary>
public sealed class SilentTcpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _clients = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;

    public SilentTcpServer()
    {
        _listener.Start();
        _acceptLoop = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _acceptLoop;
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_clients)
                {
                    _clients.Add(client);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }
    }
}
