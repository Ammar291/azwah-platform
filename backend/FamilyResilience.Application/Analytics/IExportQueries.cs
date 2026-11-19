namespace FamilyResilience.Application.Analytics;

/// <summary>
/// The fixed reads behind research exports. Both return only members whose research-use consent is active now and whose
/// household has not withdrawn; neither takes a column, an expression or SQL from a request. At most <c>limit</c> rows
/// come back, so a caller can ask for one more than it allows and tell an export that is too large.
/// </summary>
public interface IExportQueries
{
    /// <summary>Submitted and scored sessions.</summary>
    Task<IReadOnlyList<ExportSessionRow>> SessionsAsync(ExportFilter filter, int limit, CancellationToken cancellationToken = default);

    /// <summary>Score results of scoring rule sets that are still Approved, complete or not.</summary>
    Task<IReadOnlyList<ExportScoreRow>> ScoresAsync(ExportFilter filter, int limit, CancellationToken cancellationToken = default);
}
