using FamilyResilience.Infrastructure.Persistence.Outbox;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static OutboxMessage Pending() => OutboxMessage.Create("test.event.v1", "{}", Now);

    [Fact]
    public void A_failed_attempt_is_retried_after_a_doubling_backoff()
    {
        var message = Pending();

        message.RecordFailure("outbox.handler_failed", 5, Now);
        Assert.Equal((1, OutboxMessageStatus.Pending, (DateTimeOffset?)Now.AddSeconds(2)), (message.RetryCount, message.Status, message.NextAttemptAt));

        message.RecordFailure("outbox.handler_failed", 5, Now);
        Assert.Equal(Now.AddSeconds(4), message.NextAttemptAt);
        Assert.Equal("outbox.handler_failed", message.LastErrorCode);
    }

    [Fact]
    public void The_last_allowed_attempt_marks_the_message_failed()
    {
        var message = Pending();

        message.RecordFailure("outbox.handler_failed", 2, Now);
        message.RecordFailure("outbox.handler_failed", 2, Now);

        Assert.Equal((2, OutboxMessageStatus.Failed, (DateTimeOffset?)null), (message.RetryCount, message.Status, message.NextAttemptAt));
    }

    [Fact]
    public void The_backoff_never_exceeds_an_hour() =>
        Assert.Equal(TimeSpan.FromHours(1), OutboxMessage.Backoff(30));

    [Fact]
    public void A_processed_message_clears_its_retry_time()
    {
        var message = Pending();
        message.RecordFailure("outbox.handler_failed", 5, Now);

        message.MarkProcessed(Now.AddMinutes(1));

        Assert.Equal((OutboxMessageStatus.Processed, (DateTimeOffset?)Now.AddMinutes(1), (DateTimeOffset?)null),
            (message.Status, message.ProcessedAt, message.NextAttemptAt));
    }

    [Fact]
    public void An_error_code_is_kept_short()
    {
        var message = Pending();

        message.RecordFailure(new string('x', 300), 5, Now);

        Assert.Equal(OutboxMessage.MaxErrorCodeLength, message.LastErrorCode!.Length);
    }
}
