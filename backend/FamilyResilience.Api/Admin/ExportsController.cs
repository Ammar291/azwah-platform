using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Api.Requests;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// De-identified research exports. Researchers only; an export can be read and downloaded by the researcher who asked
/// for it and by no one else (anyone else gets 404). The file is written in the background from members with active
/// research-use consent, with per-export pseudonyms and coarsened values; downloads are audited. Without configured
/// storage, exports are refused (409).
/// </summary>
[ApiController]
[Route("api/v1/admin/exports")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ExportsController : ControllerBase
{
    /// <summary>Far above a request of 30 column codes.</summary>
    private const long MaxRequestBody = 16 * 1024;

    [HttpPost]
    [Authorize(Policy = Policies.Researcher)]
    [EnableRateLimiting(RateLimitPolicies.Exports)]
    [RequestSizeLimit(MaxRequestBody)]
    [EndpointSummary("Request a CSV export of submitted sessions or score results. Needs an Idempotency-Key header; 202 with the export's location while the file is written.")]
    [ProducesResponseType<ExportResponse>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ExportResponse>> Create(
        CreateExportRequest request,
        [FromHeader(Name = IdempotencyHeader.Name)] string? idempotencyKey,
        [FromServices] CreateExport useCase,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyHeader.TryParse(idempotencyKey, out var key))
        {
            return IdempotencyHeader.Invalid(this);
        }

        var result = await useCase.HandleAsync(request, key, cancellationToken);
        return result.IsSuccess ? Accepted($"/api/v1/admin/exports/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet]
    [Authorize(Policy = Policies.Researcher)]
    [EndpointSummary("The exports the caller requested, newest first, with their status and download path.")]
    [ProducesResponseType<PagedResponse<ExportResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ExportResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListExports useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("catalog")]
    [Authorize(Policy = Policies.Researcher)]
    [EndpointSummary("The datasets an export can be made of, the only columns each may contain, and the filters a request may set.")]
    [ProducesResponseType<ExportCatalogResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ExportCatalogResponse>> Catalog(
        [FromServices] GetExportCatalog useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{exportId:guid}")]
    [Authorize(Policy = Policies.ExportRequester)]
    [EndpointSummary("An export's status, row count, error code, expiry and download path.")]
    [ProducesResponseType<ExportResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ExportResponse>> Get(
        Guid exportId, [FromServices] GetExport useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(exportId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{exportId:guid}/file")]
    [Authorize(Policy = Policies.ExportRequester)]
    [EnableRateLimiting(RateLimitPolicies.ExportDownloads)]
    [EndpointSummary("The export's CSV file, while it is completed and unexpired. Each download is audited.")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Download(
        Guid exportId, [FromServices] DownloadExport useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(exportId, cancellationToken);
        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        Response.Headers.CacheControl = "no-store";
        return File(result.Value.Content, "text/csv", result.Value.FileName);
    }
}
