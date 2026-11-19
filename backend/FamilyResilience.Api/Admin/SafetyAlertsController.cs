using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Safeguarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// Safety alerts for system administrators (D-135, D-137): the queue with household references, the counts the
/// dashboard polls, and acknowledging and resolving an alert. Alerts hold fixed codes only, never what a person said.
/// Programme administrators never see them, because a score alert reveals a score (D-62). Reads of the queue,
/// acknowledgements and resolutions are audited; the counts are not.
/// </summary>
[ApiController]
[Route("api/v1/admin/safety-alerts")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SafetyAlertsController : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Safety alerts with their household reference: open, then acknowledged, then resolved; critical first; the latest detection first; optionally by status and concern.")]
    [ProducesResponseType<PagedResponse<AdminSafetyAlertResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<AdminSafetyAlertResponse>>> List(
        [FromQuery] SafetyAlertsQuery request, [FromServices] ListSafetyAlerts useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("summary")]
    [EnableRateLimiting(RateLimitPolicies.StaffReads)]
    [EndpointSummary("Counts of open, acknowledged and open critical alerts, and the latest detection among unresolved ones. No personal data; not audited.")]
    [ProducesResponseType<SafetyAlertSummaryResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<SafetyAlertSummaryResponse>> Summary(
        [FromServices] GetSafetyAlertSummary useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{alertId:guid}/acknowledge")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Acknowledge an open safety alert: a staff member is handling it.")]
    [ProducesResponseType<AdminSafetyAlertResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AdminSafetyAlertResponse>> Acknowledge(
        Guid alertId, [FromServices] AcknowledgeSafetyAlert useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(alertId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{alertId:guid}/resolve")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Resolve an open or acknowledged safety alert with its outcome.")]
    [ProducesResponseType<AdminSafetyAlertResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AdminSafetyAlertResponse>> Resolve(
        Guid alertId, ResolveSafetyAlertRequest request, [FromServices] ResolveSafetyAlert useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(alertId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
