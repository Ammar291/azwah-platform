using System.Globalization;
using FamilyResilience.Api.Hosting;
using FamilyResilience.Api.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FamilyResilience.Api.Authorization;

/// <summary>
/// The rules every bearer token must pass, applied after the framework has bound "Authentication:Schemes:Bearer" (that
/// binding replaces <see cref="TokenValidationParameters"/>, so these must run later). Issuer, audience, lifetime and
/// signature are always checked; only asymmetric algorithms are accepted, plus HS256 in Development for
/// <c>dotnet user-jwts</c>; the clock skew is a minute; a rejected token is logged by a fixed reason and counted, never
/// quoted, and the challenge does not describe it.
/// </summary>
public static partial class TokenValidation
{
    public const string LogCategory = "FamilyResilience.Api.Authorization.TokenValidation";

    /// <summary>The claim the development identity provider puts in every token it issues.</summary>
    public const string DevelopmentOnlyClaim = "azwah_development_only";

    /// <summary>Seconds of clock difference tolerated between the issuer and the API (default 60, at most 300).</summary>
    public const string ClockSkewSetting = "Authentication:ClockSkewSeconds";

    private const int DefaultClockSkewSeconds = 60;
    private const int MaxClockSkewSeconds = 300;

    private static readonly string[] AsymmetricAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    public static IReadOnlyList<string> AllowedAlgorithms(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() ? [.. AsymmetricAlgorithms, SecurityAlgorithms.HmacSha256] : AsymmetricAlgorithms;
    }

    public static TimeSpan ClockSkew(string? configured) =>
        TimeSpan.FromSeconds(int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Clamp(seconds, 0, MaxClockSkewSeconds)
            : DefaultClockSkewSeconds);

    /// <summary>A fixed category for a rejected token. Exception messages can quote claims, so they are never logged.</summary>
    public static string RejectionReason(Exception exception) => exception switch
    {
        SecurityTokenExpiredException => "expired",
        SecurityTokenInvalidSignatureException or SecurityTokenSignatureKeyNotFoundException => "signature",
        SecurityTokenInvalidIssuerException => "issuer",
        SecurityTokenInvalidAudienceException => "audience",
        SecurityTokenNotYetValidException or SecurityTokenNoExpirationException or SecurityTokenInvalidLifetimeException => "lifetime",
        SecurityTokenInvalidAlgorithmException => "algorithm",
        SecurityTokenMalformedException or ArgumentException => "malformed",
        _ => "other",
    };

    public static void Apply(JwtBearerOptions options, IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var parameters = options.TokenValidationParameters;
        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidateLifetime = true;
        parameters.ValidateIssuerSigningKey = true;
        parameters.RequireExpirationTime = true;
        parameters.RequireSignedTokens = true;
        parameters.ClockSkew = ClockSkew(configuration[ClockSkewSetting]);
        parameters.ValidAlgorithms = AllowedAlgorithms(environment);
        options.IncludeErrorDetails = false;

        var events = options.Events ?? new JwtBearerEvents();
        var tokenValidated = events.OnTokenValidated;
        events.OnTokenValidated = async context =>
        {
            // Defence in depth: even if a deployed API were pointed at the development identity provider, its tokens
            // would sign no one in.
            if (environment.IsDeployed() && context.Principal?.FindFirst(DevelopmentOnlyClaim) is not null)
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
                LogRejected(logger, "development_token");
                context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().TokenRejected("development_token");
                context.Fail("A development token is not accepted here.");
                return;
            }

            await tokenValidated(context);
        };
        var authenticationFailed = events.OnAuthenticationFailed;
        events.OnAuthenticationFailed = async context =>
        {
            var reason = RejectionReason(context.Exception);
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory);
            LogRejected(logger, reason);
            context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().TokenRejected(reason);
            await authenticationFailed(context);
        };
        options.Events = events;
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning, Message = "Rejected a bearer token: {Reason}")]
    private static partial void LogRejected(ILogger logger, string reason);
}
