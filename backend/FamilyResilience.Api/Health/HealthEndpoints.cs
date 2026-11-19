using FamilyResilience.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FamilyResilience.Api.Health;

public static class HealthEndpoints
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: it only shows that the process is serving requests.
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteStatusOnlyAsync,
        }).AllowAnonymous();

        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
            ResponseWriter = WriteStatusOnlyAsync,
        }).AllowAnonymous();

        return endpoints;
    }

    // Only the overall status leaves the server: check names, timings and exception messages stay in the logs.
    private static Task WriteStatusOnlyAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()));
}

public sealed record HealthResponse(string Status);
