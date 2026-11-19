using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// The referral queue for support coordinators, and the record of a manual hand-off to a partner organisation. The
/// coordinator passes the referral on outside the platform; the platform sends the partner nothing (D-61). Reads and
/// hand-offs are audited.
/// </summary>
[ApiController]
[Route("api/v1/admin/referrals")]
[Authorize(Policy = Policies.SupportCoordinator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ReferralCoordinationController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Referrals with their household reference, category, status and hand-off, newest first, optionally by status.")]
    [ProducesResponseType<PagedResponse<CoordinatedReferralResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<CoordinatedReferralResponse>>> List(
        [FromQuery] CoordinatedReferralsRequest request, [FromServices] ListCoordinatedReferrals useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{referralId:guid}/hand-off")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Record that you handed an open referral to a partner organisation outside the platform (a manual hand-off). Needs the member's active referral consent.")]
    [ProducesResponseType<CoordinatedReferralResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<CoordinatedReferralResponse>> HandOff(
        Guid referralId, HandOffRequest request, [FromServices] HandOffReferral useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(referralId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
