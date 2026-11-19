using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Scoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Scoring;

/// <summary>Scores from approved scoring rule sets only. Staff only; results are sensitive personal information.</summary>
[ApiController]
[Route("api/v1/assessment-sessions/{sessionId:guid}")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ScoringController : ControllerBase
{
    [HttpPost("score")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Score a submitted session with the approved scoring rule set for its scope. 409 while none is approved.")]
    [ProducesResponseType<ScoreResultResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ScoreResultResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ScoreResultResponse>> Score(
        Guid sessionId, [FromServices] ScoreSession useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return result.Value.Created
            ? Created($"/api/v1/assessment-sessions/{sessionId}/score-results", result.Value.Result)
            : Ok(result.Value.Result);
    }

    [HttpGet("score-results")]
    [EndpointSummary("List the score results recorded for a session.")]
    [ProducesResponseType<IReadOnlyList<ScoreResultResponse>>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<ScoreResultResponse>>> List(
        Guid sessionId, [FromServices] ListScoreResults useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
