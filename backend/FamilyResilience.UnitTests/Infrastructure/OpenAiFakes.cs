using System.Net;
using System.Text;
using System.Threading.Channels;
using FamilyResilience.Infrastructure.Assistant;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>
/// Answers each request with the next queued answer (the last repeats; by default one status and body), and records
/// what was sent. Nothing leaves the test.
/// </summary>
internal sealed class FakeOpenAiTransport(HttpStatusCode status, string body) : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();

    public List<SentRequest> Requests { get; } = [];

    /// <summary>How many of the next requests fail as a dropped connection before any answer.</summary>
    public int FailNext { get; set; }

    /// <summary>Answers to give, in order, before the default one.</summary>
    public FakeOpenAiTransport Then(HttpStatusCode next, string nextBody)
    {
        _answers.Enqueue((next, nextBody));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(new SentRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
        if (FailNext > 0)
        {
            FailNext--;
            throw new HttpRequestException("synthetic dropped connection");
        }

        var (answerStatus, answerBody) = _answers.Count > 0 ? _answers.Dequeue() : (status, body);
        return new HttpResponseMessage(answerStatus) { Content = new StringContent(answerBody, Encoding.UTF8, "application/json") };
    }

    public sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string Body);
}

/// <summary>Every formatted log line, so a test can show that no content reached a log.</summary>
internal sealed class CapturedLogs<T> : ILogger<T>
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (lines)
            {
                return [.. lines];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (lines)
        {
            lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}

/// <summary>
/// A WebSocket to OpenAI that is only queues: a test pushes the events OpenAI would send and reads what the code sent.
/// Nothing leaves the test.
/// </summary>
internal sealed class FakeRealtimeConnection : IRealtimeConnection
{
    private readonly Channel<byte[]?> _incoming = Channel.CreateUnbounded<byte[]?>();
    private readonly Channel<string> _sent = Channel.CreateUnbounded<string>();
    private readonly List<string> _all = [];

    public bool Disposed { get; private set; }

    /// <summary>Every event sent so far, as JSON text.</summary>
    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_all)
            {
                return [.. _all];
            }
        }
    }

    /// <summary>Called with each sent event; may push a reply.</summary>
    public Action<string>? OnSend { get; set; }

    public void Push(string json) => _incoming.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    /// <summary>OpenAI closes the socket normally.</summary>
    public void Close() => _incoming.Writer.TryWrite(null);

    /// <summary>The next sent event whose type is <paramref name="type"/>, within ten seconds.</summary>
    public async Task<string> NextSentAsync(string type, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var json = await _sent.Reader.ReadAsync(timeout.Token);
            if (json.Contains($"\"type\":\"{type}\"", StringComparison.Ordinal))
            {
                return json;
            }
        }
    }

    public Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = Encoding.UTF8.GetString(utf8Json.Span);
        lock (_all)
        {
            _all.Add(json);
        }

        _sent.Writer.TryWrite(json);
        OnSend?.Invoke(json);
        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken) => await _incoming.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Hands out one prepared <see cref="FakeRealtimeConnection"/> and records the address and key.</summary>
internal sealed class FakeRealtimeConnector(FakeRealtimeConnection connection) : IRealtimeConnector
{
    public List<(Uri Address, string ApiKey)> Connections { get; } = [];

    public Task<IRealtimeConnection> ConnectAsync(Uri address, string apiKey, CancellationToken cancellationToken)
    {
        Connections.Add((address, apiKey));
        return Task.FromResult<IRealtimeConnection>(connection);
    }
}
