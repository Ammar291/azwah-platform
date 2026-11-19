using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.SupportResources;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FamilyResilience.Api.Observability;

public static class ObservabilityExtensions
{
    public const string ServiceName = "familyresilience-api";

    /// <summary>
    /// JSON console logs plus OpenTelemetry traces, metrics and logs. Nothing leaves the process unless
    /// OTEL_EXPORTER_OTLP_ENDPOINT is set. Request bodies, query strings and headers are never logged.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        });

        builder.Services.AddMetrics();
        builder.Services.AddSingleton<SecurityMetrics>();
        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(SecurityMetrics.MeterName)
                .AddMeter(SupportResourceMetrics.MeterName)
                .AddMeter(AssistantMetrics.MeterName)
                .AddMeter(SafeguardingMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        return builder;
    }
}
