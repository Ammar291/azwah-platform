using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// Program monitoring for program administrators: who is taking part and how far their assessments have got. No
/// answers, scores or member details; every read is audited.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Policy = Policies.ProgramAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class MonitoringController : ControllerBase
{
    [HttpGet("participation")]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Households with their status, roster confirmation and assignment progress, newest first.")]
    [ProducesResponseType<PagedResponse<ParticipationRowResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ParticipationRowResponse>>> Participation(
        [FromQuery] ParticipationRequest request, [FromServices] ListParticipation useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("assessments")]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Assignments with their session progress, newest first. Shows whether a session was scored, never the score.")]
    [ProducesResponseType<PagedResponse<AssessmentRowResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<AssessmentRowResponse>>> Assessments(
        [FromQuery] AssessmentMonitoringRequest request, [FromServices] ListAssessments useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
