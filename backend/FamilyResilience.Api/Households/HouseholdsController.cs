using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Households;

/// <summary>
/// A participant's household and roster. Creating needs the Participant policy; everything else needs the caller to be
/// the household's active manager. Another family's household answers 404, exactly like one that does not exist.
/// </summary>
[ApiController]
[Route("api/v1/households")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class HouseholdsController : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.Participant)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Create the caller's household from its roster. The caller becomes its manager, linked to the member marked isSelf.")]
    [ProducesResponseType<HouseholdResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<HouseholdResponse>> Create(
        CreateHouseholdRequest request, [FromServices] CreateHousehold useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/households/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{householdId:guid}")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EndpointSummary("Get the household.")]
    [ProducesResponseType<HouseholdResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<HouseholdResponse>> Get(
        Guid householdId, [FromServices] GetHousehold useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPatch("{householdId:guid}")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Withdraw the household from the study ({\"status\": \"withdrawn\"}). Its history is kept.")]
    [ProducesResponseType<HouseholdResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<HouseholdResponse>> Update(
        Guid householdId, UpdateHouseholdRequest request, [FromServices] UpdateHousehold useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{householdId:guid}/members")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EndpointSummary("List the roster, active and inactive members, a page at a time.")]
    [ProducesResponseType<PagedResponse<MemberResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<MemberResponse>>> ListMembers(
        Guid householdId, [FromQuery] PageRequest request, [FromServices] ListMembers useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{householdId:guid}/members/{memberId:guid}")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EndpointSummary("Get one member of the roster.")]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<MemberResponse>> GetMember(
        Guid householdId, Guid memberId, [FromServices] GetMember useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, memberId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{householdId:guid}/members")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Add a member. The roster then needs confirming again.")]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<MemberResponse>> AddMember(
        Guid householdId, MemberDetailsRequest request, [FromServices] AddMember useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, request, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/households/{householdId}/members/{result.Value.Id}", result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpPost("{householdId:guid}/confirm-roster")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Confirm the roster and assign the current wave from approved eligibility rules, in one transaction.")]
    [ProducesResponseType<RosterConfirmationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RosterConfirmationResponse>> ConfirmRoster(
        Guid householdId, [FromServices] ConfirmRoster useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPatch("{householdId:guid}/members/{memberId:guid}")]
    [Authorize(Policy = Policies.HouseholdManager)]
    [EnableRateLimiting(RateLimitPolicies.ParticipantWrites)]
    [EndpointSummary("Merge-patch a member (absent fields unchanged, null clears), or remove them with {\"status\": \"inactive\"}.")]
    [ProducesResponseType<MemberResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<MemberResponse>> UpdateMember(
        Guid householdId, Guid memberId, UpdateMemberRequest request, [FromServices] UpdateMember useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(householdId, memberId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
