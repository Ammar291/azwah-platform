using FamilyResilience.Api.Authorization;
using FamilyResilience.Application.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Assistant;

/// <summary>The organisations on the All services page, from the support knowledge graph (D-115).</summary>
[ApiController]
[Route("api/v1/support-authorities")]
[Authorize(Policy = Policies.SupportResourceReader)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class SupportAuthoritiesController : ControllerBase
{
    [HttpGet]
    [EndpointSummary("Every organisation in the knowledge graph that is not past its review date, with its services, in both languages.")]
    [ProducesResponseType<IReadOnlyList<SupportAuthorityResponse>>(StatusCodes.Status200OK, "application/json")]
    public ActionResult<IReadOnlyList<SupportAuthorityResponse>> List([FromServices] ListSupportAuthorities useCase) =>
        Ok(useCase.Handle());
}
