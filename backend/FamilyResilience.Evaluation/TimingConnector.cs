using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FamilyResilience.Infrastructure.Assistant;

namespace FamilyResilience.Evaluation;

/// <summary>
/// Times how long the WebSocket to OpenAI takes to connect, so a slow session start can be told apart from a slow model,
/// and prints each <c>error</c> and <c>session.closed</c> event OpenAI sends, so a failing call shows why (D-141). This is
/// the evaluation's console only: its calls are synthetic. The API logs fixed codes only.
/// </summary>
internal sealed class TimingConnector(IRealtimeConnector inner) : IRealtimeConnector
{
    private static int sessions;

    public static TimeSpan Last { get; private set; }

    public async Task<IRealtimeConnection> ConnectAsync(Uri address, string apiKey, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var connection = await inner.ConnectAsync(address, apiKey, cancellationToken);
        Last = clock.Elapsed;
        return new Watched(connection, Interlocked.Increment(ref sessions), clock);
    }

    private sealed class Watched(IRealtimeConnection inner, int number, Stopwatch clock) : IRealtimeConnection
    {
        public Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken) => inner.SendAsync(utf8Json, cancellationToken);

        public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
        {
            byte[]? message;
            try
            {
                message = await inner.ReceiveAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Console.WriteLine($"    [openai #{number} {clock.Elapsed.TotalSeconds:0.00} s] socket: {exception.GetType().Name}");
                throw;
            }

            if (message is null)
            {
                Console.WriteLine($"    [openai #{number} {clock.Elapsed.TotalSeconds:0.00} s] socket closed normally");
            }
            else if (message.Length < 4_096 && Encoding.UTF8.GetString(message) is var text
                && (text.Contains("\"type\":\"error\"", StringComparison.Ordinal) || text.Contains("\"type\":\"session.closed\"", StringComparison.Ordinal)))
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var detail = root.TryGetProperty("error", out var error) ? error.GetRawText() : root.TryGetProperty("reason", out var reason) ? $"reason {reason}" : string.Empty;
                Console.WriteLine($"    [openai #{number} {clock.Elapsed.TotalSeconds:0.00} s] {root.GetProperty("type").GetString()} {detail}");
            }

            return message;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
