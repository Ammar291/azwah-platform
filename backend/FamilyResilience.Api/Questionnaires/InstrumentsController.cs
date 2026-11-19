using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.Questionnaires;

/// <summary>Instruments and content import. Staff only; nothing here approves content.</summary>
[ApiController]
[Route("api/v1/instruments")]
[Authorize(Policy = Policies.SystemAdministrator)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class InstrumentsController : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Create an instrument. Its Arabic name and description may be added later.")]
    [ProducesResponseType<InstrumentResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentResponse>> Create(
        CreateInstrumentRequest request, [FromServices] CreateInstrument useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Created($"/api/v1/instruments/{result.Value.Id}", result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet]
    [EndpointSummary("List instruments, a page at a time.")]
    [ProducesResponseType<PagedResponse<InstrumentResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<InstrumentResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListInstruments useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{instrumentId:guid}")]
    [EndpointSummary("Get an instrument.")]
    [ProducesResponseType<InstrumentResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<InstrumentResponse>> Get(
        Guid instrumentId, [FromServices] GetInstrument useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(instrumentId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{instrumentId:guid}/versions")]
    [EndpointSummary("List an instrument's versions without their content, newest version first.")]
    [ProducesResponseType<PagedResponse<InstrumentVersionSummaryResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<InstrumentVersionSummaryResponse>>> Versions(
        Guid instrumentId, [FromQuery] PageRequest request, [FromServices] ListInstrumentVersions useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(instrumentId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPut("{instrumentId:guid}/text")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Replace an instrument's name and description. Both languages are required.")]
    [ProducesResponseType<InstrumentResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentResponse>> ChangeText(
        Guid instrumentId, ChangeInstrumentTextRequest request, [FromServices] ChangeInstrumentText useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(instrumentId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpPost("{instrumentId:guid}/versions")]
    [EnableRateLimiting(RateLimitPolicies.StaffWrites)]
    [EndpointSummary("Import a content document as the instrument's next draft version.")]
    [ProducesResponseType<InstrumentVersionResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<InstrumentVersionResponse>> Import(
        Guid instrumentId, InstrumentContentDocument document, [FromServices] ImportInstrumentVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(instrumentId, document, cancellationToken);
        return result.IsSuccess
            ? Created($"/api/v1/instrument-versions/{result.Value.Id}", result.Value)
            : this.ToProblem(result.Error!);
    }
}
