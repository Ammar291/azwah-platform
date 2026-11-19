namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// Consumes outbox messages of one type. The dispatcher may deliver a message more than once (after a crash, or a retry
/// that follows a timeout), so a handler must be idempotent. A handler must not send sensitive data to any provider
/// outside the platform. Throwing counts as a failed attempt; retries are bounded.
/// </summary>
public interface IOutboxMessageHandler
{
    /// <summary>The outbox type code handled, for example <c>referrals.support_referral_created.v1</c>.</summary>
    string Type { get; }

    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}
