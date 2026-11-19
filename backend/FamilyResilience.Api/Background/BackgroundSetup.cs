namespace FamilyResilience.Api.Background;

public static class BackgroundSetup
{
    public const string EnabledKey = "BackgroundJobs:Enabled";

    /// <summary>
    /// The outbox dispatcher and the maintenance service. Each reads <c>BackgroundJobs:Enabled</c> when it starts and does
    /// nothing when it is false (the test host turns them off and drives the processor directly), because configuration
    /// added by a host builder may not be visible yet while services are registered. No message broker is involved.
    /// </summary>
    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcherService>();
        services.AddHostedService<MaintenanceService>();
        return services;
    }

    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue(EnabledKey, true);
}
