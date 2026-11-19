namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Where export files live. Keys come from <c>ExportJob.StorageKey</c>, never from a caller. Writing replaces any earlier
/// file of the same key in one step, so a retried export never leaves a partial or duplicated file. The local
/// implementation is for development and tests only; production needs approved private storage (open question 6), and
/// without it <see cref="IsConfigured"/> is false and exports are refused.
/// </summary>
public interface IExportFileStore
{
    bool IsConfigured { get; }

    Task WriteAsync(string key, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default);

    /// <summary>The file for reading, or null when there is none.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the file; a missing file is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
