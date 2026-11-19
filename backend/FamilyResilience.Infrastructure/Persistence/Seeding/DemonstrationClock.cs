namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// The demonstration seed's simulated clock: the services it runs read "now" from it, so each step happens at the time
/// the synthetic timeline gives it (domain methods are given the same time explicitly).
/// </summary>
internal sealed class DemonstrationClock : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
