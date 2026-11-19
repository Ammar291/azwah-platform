using FamilyResilience.Api.Identity;
using FamilyResilience.Api.Observability;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace FamilyResilience.Api.Authorization;

/// <summary>
/// Answers 404 instead of 403 when the only requirements that failed are resource requirements, so a caller cannot
/// tell another household's resource from one that does not exist. A failed role requirement still answers 403.
/// Every refusal of a signed-in caller is audited with outcome Denied: <c>authorization.resource.denied</c> against the
/// resource's type and route ID, or <c>authorization.role.denied</c> against the endpoint's route template. So probing
/// for other households' IDs, or for staff endpoints, leaves a trail. Anonymous refusals (401) are not written to the
/// database; the rate limit and the rejected-token log cover them. A refusal that cannot be staged as an audit event is
/// logged with the error code and still answered as the refusal it is. Audited refusals are also counted in
/// <see cref="SecurityMetrics"/> by kind (<c>resource</c> or <c>role</c>).
/// </summary>
public sealed partial class AuthorizationResultHandler(
    IProblemDetailsService problemDetailsService,
    SecurityMetrics metrics,
    ILogger<AuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    public const string ResourceDenied = "authorization.resource.denied";
    public const string RoleDenied = "authorization.role.denied";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure is { FailCalled: false } failure
            && failure.FailedRequirements.Any()
            && failure.FailedRequirements.All(requirement => requirement is IHiddenResourceRequirement))
        {
            var hidden = (IHiddenResourceRequirement)failure.FailedRequirements.First();
            await AuditDeniedAsync(context, ResourceDenied, "resource", hidden.ResourceType, context.GetRouteValue(hidden.RouteKey)?.ToString());
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Detail = hidden.NotFound.Message,
                    Extensions = { ["code"] = hidden.NotFound.Code },
                },
            });
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await AuditDeniedAsync(context, RoleDenied, "role", "endpoint", (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task AuditDeniedAsync(HttpContext context, string action, string kind, string resourceType, string? resourceId)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.User.FindFirst(ClaimNames.Subject) is null)
        {
            return;
        }

        metrics.AuthorizationDenied(kind);

        var bounded = resourceId is { Length: > AuditEvent.MaxResourceIdLength } ? resourceId[..AuditEvent.MaxResourceIdLength] : resourceId;
        var recorded = await context.RequestServices.GetRequiredService<IAuditLog>()
            .RecordAsync(new AuditRecord(action, resourceType, bounded, AuditOutcome.Denied), context.RequestAborted);
        if (recorded.IsFailure)
        {
            LogNotAudited(logger, recorded.Error!.Code);
        }
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Error, Message = "A refused request could not be audited: {Code}.")]
    private static partial void LogNotAudited(ILogger logger, string code);
}
