using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.SupportResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.SupportResources;

/// <summary>
/// Participants and staff find organisations in the approved directory, by search or by listing it. The answer is
/// information about public services, not an assessment, a diagnosis or a referral. Search text travels in the body and
/// is never stored or logged.
/// </summary>
[ApiController]
[Route("api/v1/support-resources")]
[Authorize(Policy = Policies.SupportResourceReader)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SupportResourcesController : ControllerBase
{
    [HttpPost("search")]
    [EnableRateLimiting(RateLimitPolicies.SupportSearches)]
    [EndpointSummary("Search the approved directory by text and filters; every match is a directory entry with its source link. When a participant's text holds a crisis signal the answer has safetyNotice \"crisis_support\", and so does the problem of a search that failed after it.")]
    [ProducesResponseType<SupportResourceDiscoveryResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<SupportResourceDiscoveryResponse>> Search(
        SearchSupportResourcesRequest request, [FromServices] SearchSupportResources useCase, CancellationToken cancellationToken)
    {
        var outcome = await useCase.HandleAsync(request, cancellationToken);

        // The crisis notice reaches the app on a failure too (D-138).
        return outcome.Result.IsSuccess
            ? Ok(outcome.Result.Value)
            : this.ToProblem(outcome.Result.Error!).WithSafetyNotice(outcome.SafetyNotice);
    }

    [HttpGet]
    [EndpointSummary("The approved directory visible to the caller, ordered by code, a page at a time; the same filters as the search.")]
    [ProducesResponseType<PagedResponse<PublishedSupportResourceResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<PublishedSupportResourceResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListPublishedSupportResources useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{resourceId:guid}")]
    [EndpointSummary("One approved directory entry; not found unless it is findable now and visible to the caller.")]
    [ProducesResponseType<PublishedSupportResourceResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublishedSupportResourceResponse>> Get(
        Guid resourceId, [FromServices] GetPublishedSupportResource useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(resourceId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
