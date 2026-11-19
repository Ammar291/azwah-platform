using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Application.Scoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Scoring;

/// <summary>
/// The family's resilience over time (D-132), for the participant app's chart. Participants only; the values go to the
/// household's head only (D-103). Results are sensitive personal information: never logged, and every read is audited.
/// </summary>
[ApiController]
[Route("api/v1/me/resilience-trend")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ResilienceTrendController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("The household assessment's recorded totals, oldest first, on the approved scale. Values for the household's head only. Each read is audited.")]
    [ProducesResponseType<ResilienceTrendResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ResilienceTrendResponse>> Get([FromServices] GetResilienceTrend useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
