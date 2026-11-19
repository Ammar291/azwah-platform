using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Assistant;

/// <summary>
/// The live Azwah consultant: a WebSocket that carries a spoken conversation (protocol version 2, see
/// <see cref="ConsultantRelay"/>). Participants only, signed in with a bearer header on the upgrade request.
/// </summary>
[ApiController]
[Route("api/v1/consultant")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ConsultantController : ControllerBase
{
    [HttpGet("live")]
    [EnableRateLimiting(RateLimitPolicies.Consultant)]
    [EndpointSummary("Upgrades to a WebSocket for a live conversation (protocol 2: the app's first message is start); with no model configured it sends {\"type\":\"unavailable\"} and closes normally. Not a WebSocket request: 400.")]
    [ProducesResponseType(StatusCodes.Status101SwitchingProtocols)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Live(
        [FromQuery] ConsultantStartRequest request,
        [FromServices] StartConsultantSession useCase,
        [FromServices] IValidator<ConsultantStartMessage> startValidator,
        [FromServices] CrisisSignalScreen screen,
        [FromServices] ISafetySignalRecorder recorder,
        [FromServices] AssistantLimits limits,
        [FromServices] AssistantMetrics metrics,
        [FromServices] TimeProvider timeProvider,
        [FromServices] ILogger<ConsultantRelay> logger)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return this.ToProblem(AssistantErrors.WebSocketRequired);
        }

        await new ConsultantRelay(useCase, startValidator, screen, recorder, limits, metrics, timeProvider, logger).RunAsync(HttpContext, request);
        return new EmptyResult();
    }
}
