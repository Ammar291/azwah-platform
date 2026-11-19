using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Support;

/// <summary>
/// The support the approved rules suggested and the referrals made with consent, for the members the caller may act
/// for. No partner integration exists: a referral is a record on the platform.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SupportController : ControllerBase
{
    [HttpGet("recommendations")]
    [EndpointSummary("Support recommendations, newest first, with the status of the referral each one led to, if any.")]
    [ProducesResponseType<PagedResponse<RecommendationResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<RecommendationResponse>>> Recommendations(
        [FromQuery] PageRequest request, [FromServices] ListMyRecommendations useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("referrals")]
    [EndpointSummary("Referrals made with referral consent, newest first, cancelled ones included.")]
    [ProducesResponseType<PagedResponse<ReferralResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ReferralResponse>>> Referrals(
        [FromQuery] PageRequest request, [FromServices] ListMyReferrals useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
