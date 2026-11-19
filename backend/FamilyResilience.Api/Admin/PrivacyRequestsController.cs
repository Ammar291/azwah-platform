using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Privacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// Families' privacy requests for program administrators (D-89): the list with household references, and closing a
/// request once it has been handled outside the platform (an erasure is run with <c>db erase-household</c>, D-88). Reads
/// and closings are audited.
/// </summary>
[ApiController]
[Route("api/v1/admin/privacy-requests")]
[Authorize(Policy = Policies.ProgramAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class PrivacyRequestsController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Privacy requests with their household reference, open ones first and then oldest first, optionally by status.")]
    [ProducesResponseType<PagedResponse<AdminPrivacyRequestResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<AdminPrivacyRequestResponse>>> List(
        [FromQuery] PrivacyRequestsQuery request, [FromServices] ListPrivacyRequests useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{requestId:guid}/close")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Close an open privacy request as completed, declined or withdrawn.")]
    [ProducesResponseType<AdminPrivacyRequestResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AdminPrivacyRequestResponse>> Close(
        Guid requestId, ClosePrivacyRequestRequest request, [FromServices] ClosePrivacyRequest useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(requestId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
