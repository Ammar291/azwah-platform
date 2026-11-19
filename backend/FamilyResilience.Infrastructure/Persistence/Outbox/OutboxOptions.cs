namespace FamilyResilience.Infrastructure.Persistence.Outbox;

/// <summary>Outbox dispatching settings from configuration section <c>Outbox</c>.</summary>
public sealed record OutboxOptions(int MaxAttempts)
{
    public const int DefaultMaxAttempts = 5;

    public static OutboxOptions Default { get; } = new(DefaultMaxAttempts);
}
