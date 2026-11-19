using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Analytics;

/// <summary>An export's status, for the researcher who requested it only.</summary>
public sealed class GetExport(ICurrentUser currentUser, IExportJobRepository jobs, TimeProvider timeProvider)
{
    public async Task<Result<ExportResponse>> HandleAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var job = await ExportJobs.RequestedByCallerAsync(jobs, currentUser, exportId, cancellationToken);
        return job.IsFailure
            ? Result.Failure<ExportResponse>(job.Error!)
            : Result.Success(job.Value.ToResponse(timeProvider.GetUtcNow()));
    }
}
