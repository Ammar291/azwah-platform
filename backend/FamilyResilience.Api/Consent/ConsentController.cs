using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Consent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Consent;

/// <summary>
/// Consent per member and purpose against approved policy versions. Participants only; the member must be one the
/// caller may act for, otherwise 404.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ConsentController : ControllerBase
{
    [HttpGet("consent/policies/current")]
    [EndpointSummary("The current approved policy of each consent type (participation, research use, referral).")]
    [ProducesResponseType<CurrentConsentPoliciesResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<CurrentConsentPoliciesResponse>> CurrentPolicies(
        [FromServices] GetCurrentConsentPolicies useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("consent")]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Grant or withdraw a member's consent for the purpose of a policy version. Granting needs the current version.")]
    [ProducesResponseType<ConsentRecordResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ConsentRecordResponse>> Record(
        RecordConsentRequest request, [FromServices] RecordConsentDecision useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("me/consents")]
    [EndpointSummary("Consent records of the members the caller may act for, newest first.")]
    [ProducesResponseType<PagedResponse<ConsentRecordResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ConsentRecordResponse>>> Mine(
        [FromQuery] PageRequest request, [FromServices] ListMyConsents useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
