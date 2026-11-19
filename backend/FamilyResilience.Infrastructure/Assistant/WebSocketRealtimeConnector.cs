using System.Buffers;
using System.Net.WebSockets;

namespace FamilyResilience.Infrastructure.Assistant;

/// <summary>
/// The real <see cref="IRealtimeConnector"/>: a <see cref="ClientWebSocket"/> to <c>wss://api.openai.com</c> with the key
/// as a bearer header, from the server only, so neither the key nor the family context ever reaches the phone (D-120). It
/// connects through <see cref="HappyEyeballs"/> (D-121) and hedges (D-124): measured on 2026-11-09, one connect in about
/// ten took 13 s and one failed outright, so a second attempt starts when the first has not connected within
/// <see cref="HedgeAfter"/> or fails, and the first to connect wins; the other is cancelled and closed.
/// </summary>
public sealed class WebSocketRealtimeConnector : IRealtimeConnector, IDisposable
{
    /// <summary>A healthy connect takes about 1 s here; past this a second attempt races the first.</summary>
    public static readonly TimeSpan HedgeAfter = TimeSpan.FromSeconds(3);

    private readonly HttpMessageInvoker invoker = new(HappyEyeballs.CreateHandler());

    public void Dispose() => invoker.Dispose();

    public async Task<IRealtimeConnection> ConnectAsync(Uri address, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var first = ConnectOnceAsync(address, apiKey, race.Token);
        var finished = await Task.WhenAny(first, Task.Delay(HedgeAfter, cancellationToken));
        if (finished == first && first.IsCompletedSuccessfully)
        {
            return first.Result;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            _ = CloseLateAsync(first);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var second = ConnectOnceAsync(address, apiKey, race.Token);
        var attempts = new List<Task<IRealtimeConnection>> { first, second };
        Exception? failure = null;
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            attempts.Remove(done);
            if (done.IsCompletedSuccessfully)
            {
                await race.CancelAsync();
                foreach (var loser in attempts)
                {
                    _ = CloseLateAsync(loser);
                }

                return done.Result;
            }

            failure = done.Exception?.GetBaseException() ?? failure;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw failure ?? new WebSocketException(WebSocketError.Faulted);
    }

    /// <summary>A losing attempt that connects anyway is closed at once; one that fails is observed.</summary>
    private static async Task CloseLateAsync(Task<IRealtimeConnection> attempt)
    {
        try
        {
            await (await attempt).DisposeAsync();
        }
        catch (Exception)
        {
            // Cancelled or failed: nothing is open.
        }
    }

    private async Task<IRealtimeConnection> ConnectOnceAsync(Uri address, string apiKey, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(address, invoker, cancellationToken);
            return new Connection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class Connection(ClientWebSocket socket) : IRealtimeConnection
    {
        /// <summary>An event larger than this is not one OpenAI sends; it ends the session rather than fill memory.</summary>
        private const int MaxEventBytes = 16 * 1024 * 1024;

        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

        private readonly SemaphoreSlim sending = new(1, 1);

        public async Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken)
        {
            await sending.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(utf8Json, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
            finally
            {
                sending.Release();
            }
        }

        public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var message = new ArrayBufferWriter<byte>(16 * 1024);
            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(16 * 1024), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return socket.CloseStatus == WebSocketCloseStatus.NormalClosure
                        ? null
                        : throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
                }

                message.Advance(result.Count);
                if (message.WrittenCount > MaxEventBytes)
                {
                    throw new WebSocketException(WebSocketError.Faulted);
                }

                if (result.EndOfMessage)
                {
                    return message.WrittenSpan.ToArray();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(CloseTimeout);
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "ended", timeout.Token);
                }
                catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
                {
                    // Closing is best effort; disposing drops the connection either way.
                }
            }

            socket.Dispose();
            sending.Dispose();
        }
    }
}
