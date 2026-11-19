using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// A written report on a session. The app writes it ahead, an area at a time as the person answers, so it is ready the
/// moment the score is (D-113); the whole-report call covers a session answered without that. These send answers to a
/// model, so the person agrees first; the server stores neither the answers it sent nor the text. Only the household's
/// head gets a report.
/// </summary>
[ApiController]
[Route("api/v1/sessions/{sessionId:guid}/report")]
[Authorize(Policy = Policies.SessionParticipant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ScoreReportController : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.ScoreReports)]
    [EndpointSummary("A written report on the recorded score, for the household's head; 503 when no model is configured, 502 when the model fails or takes over 90 seconds.")]
    [ProducesResponseType<ScoreReportResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ScoreReportResponse>> Create(
        Guid sessionId, ScoreReportRequest request, [FromServices] GenerateScoreReport useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("eligibility")]
    [EndpointSummary("Whether a report can be written ahead for this session: a model is configured, the caller is the household's head, and approved scoring rules cover it. Sends nothing.")]
    [ProducesResponseType<ScoreReportEligibilityResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<ScoreReportEligibilityResponse>> Eligibility(
        Guid sessionId, [FromServices] GetScoreReportEligibility useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("parts/{sectionCode}")]
    [EnableRateLimiting(RateLimitPolicies.ScoreReportParts)]
    [EndpointSummary("A model's text about one fully answered area, from the answers saved on the server; 409 until it is answered, 503 when no model is configured, 502 when the model fails or takes over 30 seconds.")]
    [ProducesResponseType<ScoreReportPartResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ScoreReportPartResponse>> Part(
        Guid sessionId, string sectionCode, ScoreReportRequest request, [FromServices] WriteScoreReportPart useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, sectionCode, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("summary")]
    [EnableRateLimiting(RateLimitPolicies.ScoreReportParts)]
    [EndpointSummary("A model's summary of a fully answered session, from the answers saved on the server; 409 until it is answered, 503 when no model is configured, 502 when the model fails or takes over 30 seconds.")]
    [ProducesResponseType<ScoreReportSummaryResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ScoreReportSummaryResponse>> Summary(
        Guid sessionId, ScoreReportRequest request, [FromServices] WriteScoreReportSummary useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(sessionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
