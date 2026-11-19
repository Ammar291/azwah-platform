using FamilyResilience.Infrastructure.Persistence.Outbox;

namespace FamilyResilience.Api.Background;

/// <summary>
/// Delivers outbox messages to their handlers, one at a time, while the API runs. When nothing is due it waits
/// <c>Outbox:PollIntervalSeconds</c> (default 5). Failures are retried by the processor with a bounded backoff; this
/// loop only keeps going. No message is sent outside the platform: only registered in-process handlers run.
/// </summary>
public sealed partial class OutboxDispatcherService(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!BackgroundSetup.IsEnabled(configuration))
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Outbox:PollIntervalSeconds", 5)));
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                processed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogDispatchFailed(logger, exception);
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "The outbox dispatcher could not take a message; it will try again.")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception);
}
