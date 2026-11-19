namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Pins "now" to <see cref="TestData.Now"/>, so use cases agree with the synthetic data's dates.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
