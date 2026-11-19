using FamilyResilience.Api.Observability;
using FamilyResilience.Application.Identity;

namespace FamilyResilience.Api.Identity;

/// <summary>
/// After authentication, has <see cref="ObserveCallerRoles"/> audit any change in the signed-in caller's roles, in its
/// own scope so nothing it stages can leak into the request's own commit. A subject and role set seen in the last ten
/// minutes is skipped (<see cref="RoleObservationCache"/>). A failure is logged and the request goes on: this detects
/// changes, it does not grant or refuse anything. Each recorded change is counted in <see cref="SecurityMetrics"/>.
/// </summary>
public sealed partial class RoleObservationMiddleware(
    RequestDelegate next,
    RoleObservationCache cache,
    IServiceScopeFactory scopes,
    SecurityMetrics metrics,
    ILogger<RoleObservationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.Identity?.IsAuthenticated == true && context.User.FindFirst(ClaimNames.Subject)?.Value is { Length: > 0 } subject)
        {
            var roles = context.User.FindAll(ClaimNames.Role).Select(claim => claim.Value).ToList();
            if (!cache.Contains(subject, roles))
            {
                await ObserveAsync(subject, roles, context.RequestAborted);
            }
        }

        await next(context);
    }

    private async Task ObserveAsync(string subject, List<string> roles, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ObserveCallerRoles>().HandleAsync(cancellationToken);
            if (result.IsSuccess)
            {
                cache.Remember(subject, roles);
                metrics.RolesChanged(result.Value);
            }
            else
            {
                LogFailed(logger, result.Error!.Code);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Error, Message = "Could not record the caller's roles: {Reason}.")]
    private static partial void LogFailed(ILogger logger, string reason);
}
