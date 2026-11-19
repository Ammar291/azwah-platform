using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Questionnaires;

/// <summary>Instrument versions: read, copy into a new draft, approve (publish), schedule an end, retire. Staff only.</summary>
[ApiController]
[Route("api/v1/instrument-versions/{versionId:guid}")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class InstrumentVersionsController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Get a version with its sections, items and options. Missing Arabic is null.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<InstrumentVersionResponse>> Get(
        Guid versionId, [FromServices] GetInstrumentVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("rule-sets")]
    [EndpointSummary("List the version's rule sets of every kind and status, by kind, code and version number.")]
    [ProducesResponseType<IReadOnlyList<RuleSetResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<RuleSetResponse>>> RuleSets(
        Guid versionId, [FromServices] ListVersionRuleSets useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("drafts")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Copy this version into the instrument's next draft version.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentVersionResponse>> CreateDraft(
        Guid versionId, [FromServices] CreateDraftFromVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/instrument-versions/{result.Value.Id}", result.Value)
            : this.ToProblem(result.Error!);
    }

    [HttpPost("approve")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Approve (publish) a draft. The caller is recorded as the approver.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentVersionResponse>> Approve(
        Guid versionId, ApproveInstrumentVersionRequest request, [FromServices] ApproveInstrumentVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("schedule-end")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Set a future end of an approved version's effective window.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentVersionResponse>> ScheduleEnd(
        Guid versionId, EffectiveToRequest request, [FromServices] ScheduleInstrumentVersionEnd useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("rule-sets")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Propose an eligibility or scoring rule set for this version. Proposed rules never run.")]
    [ProducesResponseType<RuleSetResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RuleSetResponse>> ProposeRuleSet(
        Guid versionId, ProposeRuleSetRequest request, [FromServices] ProposeRuleSet useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, request, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/rule-sets/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("retire")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Retire an approved version from now or an earlier time.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentVersionResponse>> Retire(
        Guid versionId, EffectiveToRequest request, [FromServices] RetireInstrumentVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(versionId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
