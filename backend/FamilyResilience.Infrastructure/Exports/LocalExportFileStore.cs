using System.Text.RegularExpressions;
using FamilyResilience.Application.Analytics;

namespace FamilyResilience.Infrastructure.Exports;

/// <summary>
/// Export files on the local file system, under <c>Exports:LocalDirectory</c>. For development and tests only: production
/// must use approved private storage (open question 6). A file is written under a temporary name and then moved over
/// the old one, so a crash part-way through never leaves a partial or second file. Keys must look like
/// <c>exports/{uuid}.csv</c> and resolve inside the root; nothing a caller sends ever reaches a path.
/// </summary>
public sealed partial class LocalExportFileStore : IExportFileStore
{
    private const int BufferSize = 64 * 1024;

    private readonly string _root;

    public LocalExportFileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public bool IsConfigured => true;

    public async Task WriteAsync(string key, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        try
        {
            return Task.FromResult<Stream?>(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, FileOptions.Asynchronous));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // Missing, or deleted by maintenance a moment ago.
            return Task.FromResult<Stream?>(null);
        }
    }

    /// <summary>Deletes the file and any temporary file a crashed write left behind for the same key.</summary>
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
        {
            foreach (var leftover in Directory.EnumerateFiles(directory, $"{Path.GetFileName(path)}.*.tmp"))
            {
                try
                {
                    File.Delete(leftover);
                }
                catch (IOException)
                {
                    // A write still holds it open; the write removes or replaces it, and a later delete catches the rest.
                }
            }
        }

        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        if (key is null || !KeyFormat().IsMatch(key))
        {
            throw new ArgumentException("Not an export storage key.", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key));
        return path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : throw new ArgumentException("The export storage key leaves the export directory.", nameof(key));
    }

    [GeneratedRegex(@"^exports/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.csv\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyFormat();
}
