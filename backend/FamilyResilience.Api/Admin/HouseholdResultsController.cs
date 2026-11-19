using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Scoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// Households' recorded results for system administrators (D-104): the total and tier each session's rule set produced.
/// Programme administrators never see scores (D-62); researchers keep their de-identified analytics. Reads are audited.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class HouseholdResultsController : ControllerBase
{
    [HttpGet("results")]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Each scored session's latest result with its household reference, wave and questionnaire, newest first.")]
    [ProducesResponseType<PagedResponse<HouseholdResultRowResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<HouseholdResultRowResponse>>> List(
        [FromQuery] HouseholdResultsRequest request, [FromServices] ListHouseholdResults useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
