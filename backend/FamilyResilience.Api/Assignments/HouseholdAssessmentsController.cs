using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Assignments;

/// <summary>
/// Eligibility and assignment for one household in one wave, from approved rules only. Staff only: decisions include
/// internal reason codes that are never shown to household members.
/// </summary>
[ApiController]
[Route("api/v1/households/{householdId:guid}")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
public sealed class HouseholdAssessmentsController : ControllerBase
{
    [HttpPost("evaluate-eligibility")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Evaluate every member against the approved eligibility rules and show what an assignment run would change.")]
    [ProducesResponseType<EligibilityEvaluationResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<EligibilityEvaluationResponse>> Evaluate(
        Guid householdId, HouseholdWaveRequest request, [FromServices] EvaluateHouseholdEligibility useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("assign")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Create the wave's assignments for eligible members and cancel not-started ones that no longer apply.")]
    [ProducesResponseType<AssignmentRunResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<AssignmentRunResponse>> Assign(
        Guid householdId, HouseholdWaveRequest request, [FromServices] AssignHouseholdForWave useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
