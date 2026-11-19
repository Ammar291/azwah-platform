using Microsoft.Extensions.Logging;

namespace FamilyResilience.Evaluation;

/// <summary>Prints the adapters' warnings, which are fixed codes only (D-126), so a failed run shows why.</summary>
internal sealed class CodeLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            Console.WriteLine($"    [{logLevel}] {formatter(state, null)}");
        }
    }
}
