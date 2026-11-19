using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using FamilyResilience.Api.Identity;
using FamilyResilience.Api.Observability;
using FamilyResilience.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyResilience.Api.RateLimiting;

/// <summary>
/// ASP.NET Core's rate limiter, between authentication and authorization so a request is counted against its token
/// subject (or its remote address when anonymous, IPv6 by /64) before any resource handler reads the database. Every
/// request passes the global limit; sensitive endpoints add a named policy (<see cref="RateLimitPolicies"/>) for signed-in
/// callers only: an anonymous caller is refused by authorization anyway, and a long-window partition per address would
/// only cost memory. Health probes are never limited. A refused request gets 429 <c>requests.rate_limited</c> with
/// <c>Retry-After</c>. Limits are per API instance: there is no shared store.
/// </summary>
public static partial class RateLimitSetup
{
    private const string LogCategory = "FamilyResilience.Api.RateLimiting";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        // Read when the limiter is built, after the host's configuration (including the test host's) is complete.
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var settings = RateLimitSettings.From(configuration);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RejectAsync;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!settings.Enabled || context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                var (key, signedIn) = Partition(context);
                return FixedWindow(key, signedIn ? settings.SignedIn : settings.Anonymous);
            });

            foreach (var (name, window) in settings.Policies)
            {
                options.AddPolicy(name, context => settings.Enabled && Partition(context) is { SignedIn: true } partition
                    ? FixedWindow(partition.Key, window)
                    : RateLimitPartition.GetNoLimiter(string.Empty));
            }
        });
        return services;
    }

    private static (string Key, bool SignedIn) Partition(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && context.User.FindFirst(ClaimNames.Subject)?.Value is { Length: > 0 } subject
            ? ($"sub:{subject}", true)
            : ($"ip:{AddressKey(context.Connection.RemoteIpAddress)}", false);

    /// <summary>An IPv4 address as is; an IPv6 address by its /64, which one subscriber usually holds whole.</summary>
    public static string AddressKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return $"{new IPAddress(bytes)}/64";
    }

    private static RateLimitPartition<string> FixedWindow(string key, RateLimitWindow window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = window.PermitLimit,
            Window = TimeSpan.FromSeconds(window.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        // The refusal came from the global limit or the endpoint's own policy; the limiter does not say which.
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "none";
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        LogRejected(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory), policy);
        context.RequestServices.GetRequiredService<SecurityMetrics>().RateLimited(policy);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Detail = RequestErrors.RateLimited.Message,
                Extensions = { ["code"] = RequestErrors.RateLimited.Code },
            },
        });
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Refused a request over a rate limit; endpoint policy {Policy}.")]
    private static partial void LogRejected(ILogger logger, string policy);
}
