using FamilyResilience.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure.Persistence.Outbox;

/// <summary>
/// Delivers one due outbox message to its handler. The message row is claimed with <c>FOR UPDATE SKIP LOCKED</c>, so
/// several API instances never deliver the same message at once, and only types that have a handler are claimed:
/// events nothing consumes (for example referrals, which have no partner integration) stay Pending. The handler runs in
/// its own scope; on success the message is marked Processed, on an exception the attempt is recorded with a short code
/// and retried after a backoff, up to <see cref="OutboxOptions.MaxAttempts"/>.
/// </summary>
public sealed partial class OutboxProcessor(
    FamilyResilienceDbContext dbContext,
    IServiceScopeFactory scopes,
    IEnumerable<IOutboxMessageHandler> handlers,
    OutboxOptions options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    public const string HandlerFailedCode = "outbox.handler_failed";

    /// <summary>Delivers the oldest due message, if any. Returns whether a message was taken.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var types = handlers.Select(handler => handler.Type).Distinct(StringComparer.Ordinal).ToArray();
        if (types.Length == 0)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var message = (await dbContext.OutboxMessages
                .FromSql(
                    $"""
                     SELECT * FROM outbox_messages
                     WHERE status = 'Pending' AND type = ANY({types})
                       AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                     ORDER BY occurred_at, id
                     LIMIT 1
                     FOR UPDATE SKIP LOCKED
                     """)
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (message is null)
        {
            return false;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().First(candidate => candidate.Type == message.Type);
            await handler.HandleAsync(new OutboxEnvelope(message.Id, message.Type, message.Payload, message.OccurredAt), cancellationToken);
            message.MarkProcessed(timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogHandlerFailed(logger, exception, message.Id, message.Type);
            message.RecordFailure(HandlerFailedCode, options.MaxAttempts, timeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Warning, Message = "Outbox message {MessageId} of type {Type} failed in its handler.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, Guid messageId, string type);
}
