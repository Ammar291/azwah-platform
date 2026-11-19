using System.Net.WebSockets;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// The live consultant's WebSocket with one sender at a time (a WebSocket allows only one send in flight). Sends and
/// receives use only the connection's own abort token: cancelling a pending WebSocket operation aborts the socket, so a
/// session that is stopping still closes politely. Nothing here inspects or logs a payload.
/// </summary>
internal sealed class ConsultantChannel(WebSocket socket, CancellationToken aborted) : IDisposable
{
    private readonly SemaphoreSlim _sending = new(1, 1);

    public Task<bool> SendTextAsync(ReadOnlyMemory<byte> json) => SendAsync(json, WebSocketMessageType.Text);

    public Task<bool> SendBinaryAsync(ReadOnlyMemory<byte> audio) => SendAsync(audio, WebSocketMessageType.Binary);

    /// <summary>Sends the close frame (without waiting for the app's), unless the socket is already closing or gone.</summary>
    public async Task CloseOutputAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            await _sending.WaitAsync(aborted);
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseOutputAsync(status, description, aborted);
                }
            }
            finally
            {
                _sending.Release();
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection is gone; there is nothing left to close.
        }
    }

    /// <summary>
    /// One whole message into <paramref name="buffer"/>: its type and length, or <c>TooBig</c> when it does not fit. A
    /// close frame from the app comes back as <see cref="WebSocketMessageType.Close"/>.
    /// </summary>
    public async Task<(WebSocketMessageType Type, int Count, bool TooBig)> ReceiveAsync(byte[] buffer)
    {
        var count = 0;
        while (true)
        {
            if (count == buffer.Length)
            {
                return (WebSocketMessageType.Binary, count, true);
            }

            var result = await socket.ReceiveAsync(buffer.AsMemory(count), aborted);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, 0, false);
            }

            count += result.Count;
            if (result.EndOfMessage)
            {
                return (result.MessageType, count, false);
            }
        }
    }

    /// <summary>Reads and drops whatever the app still sends, until its close frame or the connection ends.</summary>
    public async Task DrainAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), aborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended without a close frame.
        }
    }

    public void Abort() => socket.Abort();

    public void Dispose() => _sending.Dispose();

    private async Task<bool> SendAsync(ReadOnlyMemory<byte> payload, WebSocketMessageType type)
    {
        try
        {
            await _sending.WaitAsync(aborted);
            try
            {
                if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
                {
                    return false;
                }

                await socket.SendAsync(payload, type, endOfMessage: true, aborted);
                return true;
            }
            finally
            {
                _sending.Release();
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
