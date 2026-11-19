using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Admin;

/// <summary>
/// The partner organisations support coordinators hand referrals to. System administrators keep the list; coordinators
/// read it. A partner is a record only: no integration exists and the platform sends a partner nothing (D-61).
/// </summary>
[ApiController]
[Route("api/v1/admin/partners")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class PartnersController : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.SystemAdministrator)]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Register a partner organisation with its names and the support categories it takes. A record only; nothing is connected.")]
    [ProducesResponseType<PartnerResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<PartnerResponse>> Register(
        RegisterPartnerRequest request, [FromServices] RegisterPartner useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/admin/partners/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet]
    [Authorize(Policy = Policies.PartnerReader)]
    [EndpointSummary("Partner organisations by code, active and inactive.")]
    [ProducesResponseType<PagedResponse<PartnerResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<PartnerResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListPartners useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{partnerId:guid}")]
    [Authorize(Policy = Policies.PartnerReader)]
    [EndpointSummary("One partner organisation.")]
    [ProducesResponseType<PartnerResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PartnerResponse>> Get(
        Guid partnerId, [FromServices] GetPartner useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(partnerId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPatch("{partnerId:guid}")]
    [Authorize(Policy = Policies.SystemAdministrator)]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Activate or deactivate a partner organisation. An inactive partner receives no new hand-offs.")]
    [ProducesResponseType<PartnerResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PartnerResponse>> Update(
        Guid partnerId, UpdatePartnerRequest request, [FromServices] UpdatePartner useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(partnerId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
