using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Admin;

/// <summary>Waves for the dashboard's filters, for researchers, program administrators and system administrators. No personal data.</summary>
[ApiController]
[Route("api/v1/admin/waves")]
[Authorize(Policy = Policies.WaveReader)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class WavesController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Waves with their code, status and window, newest opening first.")]
    [ProducesResponseType<PagedResponse<WaveResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<WaveResponse>>> List(
        [FromQuery] PageRequest request, [FromServices] ListWaves useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
