using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Questionnaires;

/// <summary>Rule sets: read, approve and retire. Only approved rule sets ever run. Staff only.</summary>
[ApiController]
[Route("api/v1/rule-sets/{ruleSetId:guid}")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class RuleSetsController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Get a rule set with its definition.")]
    [ProducesResponseType<RuleSetResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<RuleSetResponse>> Get(Guid ruleSetId, [FromServices] GetRuleSet useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(ruleSetId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("approve")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Approve a proposed rule set for its approved version. The caller is recorded as the approver.")]
    [ProducesResponseType<RuleSetResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RuleSetResponse>> Approve(
        Guid ruleSetId, ApproveRuleSetRequest request, [FromServices] ApproveRuleSet useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(ruleSetId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("retire")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Retire an approved rule set so it never runs again. The retirement time is the server's clock.")]
    [ProducesResponseType<RuleSetResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RuleSetResponse>> Retire(
        Guid ruleSetId, [FromServices] RetireRuleSet useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(ruleSetId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
