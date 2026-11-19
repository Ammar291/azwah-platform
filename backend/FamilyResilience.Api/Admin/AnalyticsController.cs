using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// De-identified aggregates for researchers and program administrators. Fixed queries only: the wave and the trend
/// window are the only inputs, small demographic cells are suppressed, and no individual's data is returned.
/// </summary>
[ApiController]
[Route("api/v1/admin/analytics")]
[Authorize(Policy = Policies.AnalyticsReader)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AnalyticsController : ControllerBase
{
    [HttpGet("overview")]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Enrolment, assignment and completion counts, completion by version, over time and by age band and employment status.")]
    [ProducesResponseType<AnalyticsOverviewResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<AnalyticsOverviewResponse>> Overview(
        [FromQuery] AnalyticsOverviewRequest request, [FromServices] GetAnalyticsOverview useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("score-distributions")]
    [Authorize(Policy = Policies.Researcher)]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Researchers only: how complete scores of each approved scoring rule set fall across its tiers, for members with research-use consent, small cells suppressed.")]
    [ProducesResponseType<ScoreDistributionsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<ScoreDistributionsResponse>> ScoreDistributions(
        [FromQuery] ScoreDistributionsRequest request, [FromServices] GetScoreDistributions useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
