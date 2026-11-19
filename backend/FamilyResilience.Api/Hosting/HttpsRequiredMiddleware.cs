using FamilyResilience.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Hosting;

/// <summary>
/// In deployed hosts, answers any request that did not arrive over HTTPS with 400 <c>requests.https_required</c>, without
/// running it. There is no redirect: a client that sent a token in clear must fail, not be sent on. Health probes are
/// exempt because orchestrators call them over plain HTTP inside the cluster.
/// </summary>
public sealed class HttpsRequiredMiddleware(RequestDelegate next, IProblemDetailsService problemDetailsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.IsHttps || context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = RequestErrors.HttpsRequired.Message,
                Extensions = { ["code"] = RequestErrors.HttpsRequired.Code },
            },
        });
    }
}
