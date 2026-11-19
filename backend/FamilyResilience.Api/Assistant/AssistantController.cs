using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// The Azwah assistant: a reply to the chat the app sends, built from what Azwah holds about the caller's family. The
/// server keeps no conversation; the chat and the reply are never stored or logged. Participants only.
/// </summary>
[ApiController]
[Route("api/v1/assistant")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AssistantController : ControllerBase
{
    [HttpGet("availability")]
    [EndpointSummary("Whether the text assistant and the live consultant each have a model configured.")]
    [ProducesResponseType<AssistantAvailabilityResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<AssistantAvailabilityResponse>> Availability(
        [FromServices] GetAssistantAvailability useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("replies")]
    [EnableRateLimiting(RateLimitPolicies.Assistant)]
    [EndpointSummary("A reply to the chat, from the caller's family context; 503 when no model is configured, 502 when the model fails or takes over 30 seconds. When the person's message holds a crisis signal the reply has safetyNotice \"crisis_support\" and SOS first, and a failure's problem (502, 503, or a 500 server fault) has the extension safetyNotice.")]
    [ProducesResponseType<AssistantReplyResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AssistantReplyResponse>> Reply(
        AssistantReplyRequest request, [FromServices] GenerateAssistantReply useCase, CancellationToken cancellationToken)
    {
        var outcome = await useCase.HandleAsync(request, cancellationToken);
        if (outcome.Result.IsSuccess)
        {
            return Ok(outcome.Result.Value);
        }

        // The crisis notice reaches the app on a failure too (D-138), beside the problem's code and trace ID.
        return this.ToProblem(outcome.Result.Error!).WithSafetyNotice(outcome.SafetyNotice);
    }
}
