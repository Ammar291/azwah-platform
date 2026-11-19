using FamilyResilience.Api.Observability;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Sessions;

namespace FamilyResilience.Api.Background;

/// <summary>
/// Small housekeeping on a timer (<c>Maintenance:IntervalMinutes</c>, default 60): it deletes idempotency keys past their
/// 24 hours and the files of research exports past their expiry, marks assignments past their expiry Expired (abandoning
/// a session still in progress on them, <see cref="ExpireOverdueAssignments"/>), then runs the retention job
/// (<see cref="ApplyRetention"/>), which deletes nothing until <c>Retention:Enabled</c> and approved periods are
/// configured (D-88, open question 22).
/// </summary>
public sealed partial class MaintenanceService(
    IServiceScopeFactory scopes, IConfiguration configuration, SecurityMetrics metrics, ILogger<MaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!BackgroundSetup.IsEnabled(configuration))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Maintenance:IntervalMinutes", 60))));
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var purged = await scope.ServiceProvider.GetRequiredService<PurgeExpiredIdempotencyKeys>().HandleAsync(stoppingToken);
                    LogPurged(logger, purged.Value);
                    var expired = await scope.ServiceProvider.GetRequiredService<ExpireExports>().HandleAsync(stoppingToken);
                    if (expired.IsSuccess)
                    {
                        LogExpired(logger, expired.Value);
                    }
                    else
                    {
                        LogExpiryFailed(logger, expired.Error!.Code);
                    }

                    var overdue = await scope.ServiceProvider.GetRequiredService<ExpireOverdueAssignments>().HandleAsync(stoppingToken);
                    if (overdue.IsSuccess && (overdue.Value.AssignmentsExpired > 0 || overdue.Value.Failures.Count > 0))
                    {
                        var closed = overdue.Value;
                        LogAssignmentsExpired(logger, closed.AssignmentsExpired, closed.SessionsAbandoned, closed.Failures.Count);
                        if (closed.Failures.Count > 0)
                        {
                            LogAssignmentExpiryFailures(logger, string.Join(", ", closed.Failures.Distinct(StringComparer.Ordinal)));
                        }
                    }

                    var retained = await scope.ServiceProvider.GetRequiredService<ApplyRetention>().HandleAsync(stoppingToken);
                    if (retained.IsSuccess && retained.Value != RetentionReport.Nothing)
                    {
                        var report = retained.Value;
                        metrics.RetentionRan(report);
                        LogRetention(logger, report.HouseholdsErased, report.OutboxMessagesDeleted, report.ExportJobsDeleted, report.Failures.Count);
                        if (report.ErasureNotConfigured)
                        {
                            LogErasureNotConfigured(logger);
                        }

                        if (report.Failures.Count > 0)
                        {
                            LogErasureFailures(logger, string.Join(", ", report.Failures.Distinct(StringComparer.Ordinal)));
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogMaintenanceFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Maintenance removed {Count} expired idempotency keys.")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Maintenance expired {Count} research exports.")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Expiring research exports stopped with {ErrorCode}; it will run again at the next interval.")]
    private static partial void LogExpiryFailed(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "Retention erased {Households} households and deleted {OutboxMessages} outbox messages and {ExportJobs} export jobs; {Failures} erasures failed.")]
    private static partial void LogRetention(ILogger logger, int households, int outboxMessages, int exportJobs, int failures);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning,
        Message = "Retention has a household period but Erasure:ConsentRecords is not set; no household was erased.")]
    private static partial void LogErasureNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Warning,
        Message = "Retention could not erase some households; they were rolled back and will be tried again: {Codes}.")]
    private static partial void LogErasureFailures(ILogger logger, string codes);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Information,
        Message = "Maintenance expired {Assignments} assignments past their expiry and abandoned {Sessions} sessions in progress on them; {Failures} could not be closed.")]
    private static partial void LogAssignmentsExpired(ILogger logger, int assignments, int sessions, int failures);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Warning,
        Message = "Some overdue assignments could not be expired; they were rolled back and will be tried again: {Codes}.")]
    private static partial void LogAssignmentExpiryFailures(ILogger logger, string codes);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "Maintenance failed; it will run again at the next interval.")]
    private static partial void LogMaintenanceFailed(ILogger logger, Exception exception);
}
