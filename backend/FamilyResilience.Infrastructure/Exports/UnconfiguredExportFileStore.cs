using FamilyResilience.Application.Analytics;

namespace FamilyResilience.Infrastructure.Exports;

/// <summary>
/// The store used when <c>Exports:LocalDirectory</c> is not set, as in production until approved private storage exists
/// (open question 6). Export requests are refused before they reach it; any call is a bug.
/// </summary>
public sealed class UnconfiguredExportFileStore : IExportFileStore
{
    public bool IsConfigured => false;

    public Task WriteAsync(string key, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw NotConfigured();

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => throw NotConfigured();

    private static InvalidOperationException NotConfigured() => new("No export storage is configured.");
}
