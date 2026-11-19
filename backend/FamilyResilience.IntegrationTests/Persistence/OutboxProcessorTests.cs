using FamilyResilience.Application.Abstractions;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// The outbox dispatcher against PostgreSQL. Each test replaces every handler (including the export worker) with one for
/// a type of its own, so the many Pending messages other tests leave behind are never claimed, and the test host's
/// fixed clock keeps retries from falling due.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxProcessorTests(ApiFactory factory)
{
    private static string UniqueType() => $"tests.outbox_{Guid.NewGuid():N}.v1";

    private static WebApplicationFactory<Program> WithHandler(ApiFactory factory, IOutboxMessageHandler handler, int? maxAttempts = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (maxAttempts is { } attempts)
            {
                builder.UseSetting("Outbox:MaxAttempts", attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxMessageHandler>();
                services.AddSingleton(handler);
            });
        });

    private async Task<OutboxMessage> SavedMessageAsync(string type, CancellationToken cancellationToken)
    {
        var message = OutboxMessage.Create(type, """{"syntheticId":"00000000-0000-0000-0000-000000000001"}""", TestData.Now.AddMinutes(-5));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<FamilyResilienceDbContext>().OutboxMessages.Add(message);
                return Task.CompletedTask;
            },
            cancellationToken);
        return message;
    }

    private Task<OutboxMessage> StoredAsync(Guid id, CancellationToken cancellationToken) =>
        factory.QueryAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id, cancellationToken));

    private static async Task<bool> ProcessNextAsync(WebApplicationFactory<Program> host, CancellationToken cancellationToken)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessNextAsync(cancellationToken);
    }

    [Fact]
    public async Task A_due_message_is_delivered_to_its_handler_and_marked_processed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHandler(UniqueType());
        await using var host = WithHandler(factory, handler);
        var message = await SavedMessageAsync(handler.Type, cancellationToken);

        var taken = await ProcessNextAsync(host, cancellationToken);
        var again = await ProcessNextAsync(host, cancellationToken);

        Assert.True(taken);
        Assert.False(again);
        var delivered = Assert.Single(handler.Delivered);
        Assert.Equal((message.Id, message.Type, message.OccurredAt), (delivered.Id, delivered.Type, delivered.OccurredAt));
        Assert.Contains("syntheticId", delivered.Payload, StringComparison.Ordinal);
        var stored = await StoredAsync(message.Id, cancellationToken);
        Assert.Equal(
            (OutboxMessageStatus.Processed, (DateTimeOffset?)TestData.Now, 0, (string?)null, (DateTimeOffset?)null),
            (stored.Status, stored.ProcessedAt, stored.RetryCount, stored.LastErrorCode, stored.NextAttemptAt));
    }

    [Fact]
    public async Task A_failing_handler_records_the_attempt_and_leaves_the_message_pending_until_its_retry_is_due()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHandler(UniqueType(), fails: true);
        await using var host = WithHandler(factory, handler);
        var message = await SavedMessageAsync(handler.Type, cancellationToken);

        var taken = await ProcessNextAsync(host, cancellationToken);
        var beforeRetry = await ProcessNextAsync(host, cancellationToken);

        Assert.True(taken);
        Assert.False(beforeRetry);
        Assert.Single(handler.Delivered);
        var stored = await StoredAsync(message.Id, cancellationToken);
        Assert.Equal(
            (OutboxMessageStatus.Pending, 1, (string?)OutboxProcessor.HandlerFailedCode, (DateTimeOffset?)TestData.Now.Add(OutboxMessage.Backoff(1)), (DateTimeOffset?)null),
            (stored.Status, stored.RetryCount, stored.LastErrorCode, stored.NextAttemptAt, stored.ProcessedAt));
    }

    [Fact]
    public async Task A_message_that_uses_its_last_attempt_becomes_failed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHandler(UniqueType(), fails: true);
        await using var host = WithHandler(factory, handler, maxAttempts: 1);
        var message = await SavedMessageAsync(handler.Type, cancellationToken);

        Assert.True(await ProcessNextAsync(host, cancellationToken));

        var stored = await StoredAsync(message.Id, cancellationToken);
        Assert.Equal(
            (OutboxMessageStatus.Failed, 1, (string?)OutboxProcessor.HandlerFailedCode, (DateTimeOffset?)null),
            (stored.Status, stored.RetryCount, stored.LastErrorCode, stored.NextAttemptAt));
    }

    [Fact]
    public async Task A_message_whose_type_has_no_handler_stays_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var handler = new RecordingHandler(UniqueType());
        await using var host = WithHandler(factory, handler);
        var unhandled = await SavedMessageAsync(UniqueType(), cancellationToken);

        var taken = await ProcessNextAsync(host, cancellationToken);

        Assert.False(taken);
        Assert.Empty(handler.Delivered);
        var stored = await StoredAsync(unhandled.Id, cancellationToken);
        Assert.Equal(
            (OutboxMessageStatus.Pending, 0, (string?)null, (DateTimeOffset?)null),
            (stored.Status, stored.RetryCount, stored.LastErrorCode, stored.ProcessedAt));
    }

    /// <summary>Records every message it receives; when <paramref name="fails"/>, throws after recording.</summary>
    private sealed class RecordingHandler(string type, bool fails = false) : IOutboxMessageHandler
    {
        private readonly List<OutboxEnvelope> _delivered = [];

        public string Type { get; } = type;

        public IReadOnlyList<OutboxEnvelope> Delivered => _delivered;

        public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
        {
            _delivered.Add(message);
            return fails ? throw new InvalidOperationException("A synthetic handler failure.") : Task.CompletedTask;
        }
    }
}
