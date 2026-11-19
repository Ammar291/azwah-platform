using FamilyResilience.Application.Analytics;

namespace FamilyResilience.Infrastructure.Exports;

/// <summary>
/// Chooses where export files go. Local storage is used only when the host allows it (development and tests) and an
/// absolute directory is configured; everything else is unconfigured, so export requests are refused (open question 6).
/// </summary>
public static class ExportFileStores
{
    public static IExportFileStore Create(string? directory, bool localStorageAllowed)
    {
        if (string.IsNullOrWhiteSpace(directory) || !localStorageAllowed)
        {
            return new UnconfiguredExportFileStore();
        }

        return Path.IsPathFullyQualified(directory)
            ? new LocalExportFileStore(directory)
            : throw new InvalidOperationException("Exports:LocalDirectory must be an absolute path outside the repository.");
    }
}
