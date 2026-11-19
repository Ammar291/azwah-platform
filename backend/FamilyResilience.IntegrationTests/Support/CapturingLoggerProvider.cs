using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Records the category, level, event and formatted message of every log entry (never its exception), so tests can prove
/// what a message leaves out.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, eventId.Id, formatter(state, null)));
    }
}

internal sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message);
