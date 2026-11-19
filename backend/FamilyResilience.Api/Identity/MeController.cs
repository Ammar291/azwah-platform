using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Identity;

/// <summary>The signed-in caller: who they are and what they may do. Any signed-in caller with a token subject.</summary>
[ApiController]
[Route("api/v1/me")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class MeController(ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("The token subject and roles, and the caller's household from the platform's records.")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<MeResponse>> Get([FromServices] GetMe useCase, CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is null)
        {
            return Forbid();
        }

        var result = await useCase.HandleAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("permissions")]
    [EndpointSummary("The permissions the caller's policies grant, and what they may do in their household. For display only: every endpoint authorises again.")]
    [ProducesResponseType<PermissionsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<PermissionsResponse>> Permissions(
        [FromServices] IAuthorizationService authorization, [FromServices] ICallerAccess access, CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is null)
        {
            return Forbid();
        }

        var granted = new List<string>();
        foreach (var policy in PermissionMap.Global.Select(entry => entry.Policy).Distinct(StringComparer.Ordinal))
        {
            if ((await authorization.AuthorizeAsync(User, policy)).Succeeded)
            {
                granted.AddRange(PermissionMap.Global.Where(entry => entry.Policy == policy).Select(entry => entry.Permission));
            }
        }

        var households = new List<HouseholdPermissionsResponse>();
        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is { } household && household.IsManagedBy(caller.UserId)
            && (await authorization.AuthorizeAsync(User, Policies.Participant)).Succeeded)
        {
            households.Add(new HouseholdPermissionsResponse(household.Id.Value, PermissionMap.HouseholdManager));
        }

        return Ok(new PermissionsResponse(granted.Order(StringComparer.Ordinal).ToList(), households));
    }

    [HttpPost("registration")]
    [Authorize(Policy = Policies.Participant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Register the caller as a platform user after they confirmed the details their identity provider showed them ({\"detailsConfirmed\": true}). 201 when this created the registration, 200 with the first registration time when it already existed. No personal details are sent or stored.")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RegistrationResponse>> Register(
        RegistrationRequest request, [FromServices] RegisterCaller useCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await useCase.HandleAsync(cancellationToken);
        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return result.Value.Created ? Created("/api/v1/me", result.Value.Response) : Ok(result.Value.Response);
    }

    [HttpPost("sign-ins")]
    [EnableRateLimiting(RateLimitPolicies.Identity)]
    [EndpointSummary("Record that the caller signed in to the admin dashboard or the mobile app (audited as identity.session.started). The identity provider's own log stays the authoritative sign-in record.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> RecordSignIn(
        SessionEventRequest request, [FromServices] RecordSessionEvent useCase, CancellationToken cancellationToken) =>
        RecordAsync(SessionEventKind.Started, request, useCase, cancellationToken);

    [HttpPost("sign-outs")]
    [EnableRateLimiting(RateLimitPolicies.Identity)]
    [EndpointSummary("Record that the caller is signing out of the admin dashboard or the mobile app (audited as identity.session.ended).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public Task<IActionResult> RecordSignOut(
        SessionEventRequest request, [FromServices] RecordSessionEvent useCase, CancellationToken cancellationToken) =>
        RecordAsync(SessionEventKind.Ended, request, useCase, cancellationToken);

    private async Task<IActionResult> RecordAsync(
        SessionEventKind kind, SessionEventRequest request, RecordSessionEvent useCase, CancellationToken cancellationToken)
    {
        if (currentUser.SubjectId is null)
        {
            return Forbid();
        }

        var result = await useCase.HandleAsync(kind, request, cancellationToken);
        return result.IsSuccess ? NoContent() : this.ToProblem(result.Error!);
    }
}
