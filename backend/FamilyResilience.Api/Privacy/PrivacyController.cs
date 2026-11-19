using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Privacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Privacy;

/// <summary>
/// A family's own data (D-89): a copy of what the platform holds about the members the caller may act for, and requests
/// for access, correction or erasure that program administrators handle. Participants only; another household's member
/// answers 404.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Authorize(Policy = Policies.Participant)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class PrivacyController : ControllerBase
{
    [HttpGet("data-export")]
    [EnableRateLimiting(RateLimitPolicies.DataExports)]
    [EndpointSummary("A copy of the caller's household and the members they may act for: consents, assignments, sessions with answers, referrals and privacy requests. Scores and recommendations are not included.")]
    [ProducesResponseType<MyDataExportResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<MyDataExportResponse>> Export([FromServices] ExportMyData useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("privacy-requests")]
    [EndpointSummary("Privacy requests of the members the caller may act for, newest first.")]
    [ProducesResponseType<PagedResponse<PrivacyRequestResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<PrivacyRequestResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListMyPrivacyRequests useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("privacy-requests")]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Ask for access to, correction of or erasure of a member's data (the caller's own member by default). One open request per member and type.")]
    [ProducesResponseType<PrivacyRequestResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PrivacyRequestResponse>> Create(
        CreatePrivacyRequestRequest request, [FromServices] CreatePrivacyRequest useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Created("/api/v1/me/privacy-requests", result.Value) : this.ToProblem(result.Error!);
    }
}
