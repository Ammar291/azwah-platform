using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Assignments;

/// <summary>
/// The assessments the caller has been asked to answer: their own, and as household manager those of members without an
/// account. Another family's assignment answers 404.
/// </summary>
[ApiController]
[Route("api/v1/assignments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AssignmentsController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.Participant)]
    [EndpointSummary("Assignments of the members the caller may act for, newest first; optionally one status.")]
    [ProducesResponseType<PagedResponse<ParticipantAssignmentResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ParticipantAssignmentResponse>>> List(
        [FromQuery] MyAssignmentsRequest request, [FromServices] ListMyAssignments useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("demo-check-in")]
    [Authorize(Policy = Policies.Participant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("DEMONSTRATIONS ONLY (D-144): a new check-in round for the caller's household at once, assigned by the approved eligibility rules in a wave of its own that is never opened, so no other household is touched; a household assessment still waiting or in progress is returned instead. Household managers only. 404 unless DemoTools:Enabled, which a deployed API refuses. Audited.")]
    [ProducesResponseType<DemoCheckInResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DemoCheckInResponse>> DemoCheckIn(
        [FromServices] OpenDemoCheckIn useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{assignmentId:guid}")]
    [Authorize(Policy = Policies.AssignmentParticipant)]
    [EndpointSummary("One assignment, with the session in progress if there is one.")]
    [ProducesResponseType<ParticipantAssignmentResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ParticipantAssignmentResponse>> Get(
        Guid assignmentId, [FromServices] GetMyAssignment useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(assignmentId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
