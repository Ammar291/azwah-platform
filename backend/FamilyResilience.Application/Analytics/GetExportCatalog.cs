using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>The datasets, columns and filters a research export may use, straight from <see cref="ExportCatalog"/>.</summary>
public sealed class GetExportCatalog
{
    public Task<Result<ExportCatalogResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var datasets = ExportCatalog.DatasetCodes
            .Select(code => new ExportDatasetResponse(code, ExportCatalog.ColumnsOf(ExportCatalog.ParseDataset(code).Value)))
            .ToList();
        return Task.FromResult(Result.Success(new ExportCatalogResponse(datasets, ExportCatalog.FilterCodes)));
    }
}
