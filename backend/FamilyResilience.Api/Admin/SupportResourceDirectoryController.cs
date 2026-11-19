using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.SupportResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// The approved support-resource directory. System administrators draft, edit, approve and retire entries; an entry
/// is findable only while approved and before its review date. The directory is a list of links to organisations
/// outside the platform: it connects to none of them.
/// </summary>
[ApiController]
[Route("api/v1/admin/support-resources")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SupportResourceDirectoryController : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Draft a directory entry: version 1 of a new code, or the next version of an existing one.")]
    [ProducesResponseType<SupportResourceResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SupportResourceResponse>> Create(
        CreateSupportResourceDraftRequest request, [FromServices] CreateSupportResourceDraft useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/admin/support-resources/{result.Value.Id}", result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpGet]
    [EndpointSummary("Directory entries by code and newest version, optionally of one status.")]
    [ProducesResponseType<PagedResponse<SupportResourceResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<SupportResourceResponse>>> List(
        [FromQuery] ListSupportResourcesRequest request, [FromServices] ListSupportResources useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{resourceId:guid}")]
    [EndpointSummary("One directory entry.")]
    [ProducesResponseType<SupportResourceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<SupportResourceResponse>> Get(
        Guid resourceId, [FromServices] GetSupportResource useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(resourceId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPut("{resourceId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Replace the content of a draft. An approved or retired version cannot change.")]
    [ProducesResponseType<SupportResourceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SupportResourceResponse>> Update(
        Guid resourceId,
        UpdateSupportResourceDraftRequest request,
        [FromServices] UpdateSupportResourceDraft useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(resourceId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{resourceId:guid}/approve")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Approve a draft with a named source and a review date; the code's previous approved version is retired.")]
    [ProducesResponseType<SupportResourceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SupportResourceResponse>> Approve(
        Guid resourceId,
        ApproveSupportResourceRequest request,
        [FromServices] ApproveSupportResource useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(resourceId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{resourceId:guid}/retire")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Take an approved entry out of the directory.")]
    [ProducesResponseType<SupportResourceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SupportResourceResponse>> Retire(
        Guid resourceId, [FromServices] RetireSupportResource useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(resourceId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
