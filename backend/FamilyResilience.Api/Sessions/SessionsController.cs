using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Api.Requests;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Sessions;

/// <summary>
/// Answering an assignment: start a session, save answers, submit, read the result. Participants only, and only for a
/// member they may act for; another family's session answers 404. Answers and results are sensitive personal
/// information: they are never logged, and reads are audited.
/// </summary>
[ApiController]
[Route("api/v1")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SessionsController : ControllerBase
{
    /// <summary>Above the largest answer batch the validator allows, far below anything costly.</summary>
    private const long MaxResponsesBody = 64 * 1024;

    [HttpPost("assignments/{assignmentId:guid}/sessions")]
    [Authorize(Policy = Policies.AssignmentParticipant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Start a session on an assignment. Needs active participation consent (403 otherwise).")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SessionResponse>> Start(
        Guid assignmentId, [FromServices] StartSession useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(assignmentId, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/sessions/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("sessions/{sessionId:guid}")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EndpointSummary("A session's state and progress.")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<SessionResponse>> Get(
        Guid sessionId, [FromServices] GetSession useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPut("sessions/{sessionId:guid}/responses")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EnableRateLimiting(RateLimitPolicies.Answers)]
    [RequestSizeLimit(MaxResponsesBody)]
    [EndpointSummary("Record or replace up to 200 answers, one per item, in one commit. Items not listed keep their answers.")]
    [ProducesResponseType<SessionAnswersResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SessionAnswersResponse>> SaveResponses(
        Guid sessionId, SaveResponsesRequest request, [FromServices] SaveResponses useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("sessions/{sessionId:guid}/responses")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EndpointSummary("The session's answers in item order. Each read is audited.")]
    [ProducesResponseType<SessionAnswersResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<SessionAnswersResponse>> GetResponses(
        Guid sessionId, [FromServices] GetResponses useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("sessions/{sessionId:guid}/demo-answers")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EndpointSummary("DEMONSTRATIONS ONLY (D-130): answers that the approved scoring rules score in the lowest, middle or highest band (level low, medium or high). Saves nothing. 404 unless DemoTools:Enabled, which a deployed API refuses. Each plan is audited.")]
    [ProducesResponseType<DemoAnswersResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<DemoAnswersResponse>> DemoAnswers(
        Guid sessionId, [FromQuery] DemoAnswersRequest request, [FromServices] PlanDemoAnswers useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("sessions/{sessionId:guid}/demo-submission")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("DEMONSTRATIONS ONLY (D-143): answers every question as the approved scoring rules score the level asked for (low, medium or high), saves the answers, then submits, scores and routes the session as a person's submission does. Needs an Idempotency-Key header; a retry with the same key returns the same outcome. 404 unless DemoTools:Enabled, which a deployed API refuses. The saved answers are audited.")]
    [ProducesResponseType<SubmissionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SubmissionResponse>> DemoSubmission(
        Guid sessionId,
        [FromHeader(Name = IdempotencyHeader.Name)] string? idempotencyKey,
        DemoAnswersRequest request,
        [FromServices] SubmitDemoAnswers useCase,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyHeader.TryParse(idempotencyKey, out var key))
        {
            return IdempotencyHeader.Invalid(this);
        }

        var result = await useCase.HandleAsync(sessionId, key, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("sessions/{sessionId:guid}/submit")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Submit, then score and route with the approved rules, in one transaction. Needs an Idempotency-Key header; a retry with the same key returns the same outcome.")]
    [ProducesResponseType<SubmissionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SubmissionResponse>> Submit(
        Guid sessionId,
        [FromHeader(Name = IdempotencyHeader.Name)] string? idempotencyKey,
        [FromServices] SubmitSession useCase,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyHeader.TryParse(idempotencyKey, out var key))
        {
            return IdempotencyHeader.Invalid(this);
        }

        var result = await useCase.HandleAsync(sessionId, key, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("sessions/{sessionId:guid}/result")]
    [Authorize(Policy = Policies.SessionParticipant)]
    [EndpointSummary("The latest result of a submitted session, or \"pending\" while no approved rule set has scored it. Each read is audited.")]
    [ProducesResponseType<SessionResultResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<SessionResultResponse>> Result(
        Guid sessionId, [FromServices] GetSessionResult useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("me/assessment-history")]
    [Authorize(Policy = Policies.Participant)]
    [EndpointSummary("Submitted check-ins of the members the caller may act for, most recent first.")]
    [ProducesResponseType<PagedResponse<AssessmentHistoryItemResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<AssessmentHistoryItemResponse>>> History(
        [FromQuery] PageRequest request, [FromServices] ListAssessmentHistory useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
