using FamilyResilience.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Endpoints that exist only inside the test host, to exercise cross-cutting behaviour.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("test-only")]
public sealed class TestOnlyController : ControllerBase
{
    public const string SensitiveDetail = "connection to 10.0.0.5 failed for user azwah_admin";

    [HttpGet("throw")]
    [AllowAnonymous]
    public IActionResult Throw() => throw new InvalidOperationException(SensitiveDetail);

    [HttpGet("bad-request/{status:int}")]
    [AllowAnonymous]
    public IActionResult ThrowBadRequest(int status) => throw new BadHttpRequestException(SensitiveDetail, status);

    [HttpPost("echo")]
    [AllowAnonymous]
    public ActionResult<EchoRequest> Echo(EchoRequest request) => request;

    [HttpGet("researcher-only")]
    [Authorize(Policy = Policies.Researcher)]
    public IActionResult ResearcherOnly() => Ok();
}
