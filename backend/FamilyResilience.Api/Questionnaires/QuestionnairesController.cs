using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Errors;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Questionnaires;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Questionnaires;

/// <summary>
/// Published questionnaire content for answering. Holds no personal data, so any platform role may read it. Drafts are
/// never shown; scoring metadata never leaves the server.
/// </summary>
[ApiController]
[Route("api/v1/questionnaires")]
[Authorize(Policy = Policies.QuestionnaireReader)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class QuestionnairesController : ControllerBase
{
    [HttpGet("available")]
    [EndpointSummary("Questionnaires whose approved version is in effect now.")]
    [ProducesResponseType<PagedResponse<AvailableQuestionnaireResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<AvailableQuestionnaireResponse>>> Available(
        [FromQuery] PageRequest request, [FromServices] ListAvailableQuestionnaires useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }

    [HttpGet("{code}/versions/{version:int}")]
    [EndpointSummary("A published version's content in display order: sections, items and answer options in Arabic and English.")]
    [ProducesResponseType<PublishedVersionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublishedVersionResponse>> Version(
        string code, int version, [FromServices] GetPublishedQuestionnaireVersion useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.HandleAsync(code, version, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
