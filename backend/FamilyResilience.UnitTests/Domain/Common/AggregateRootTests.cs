using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain.Common;

public sealed class AggregateRootTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Raised_events_are_kept_until_cleared()
    {
        var aggregate = new TestAggregate();

        aggregate.DoSomething(Now);

        var raised = Assert.IsType<SomethingHappened>(Assert.Single(aggregate.GetDomainEvents()));
        Assert.Equal(Now, raised.OccurredAt);
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.GetDomainEvents());
    }

    [Fact]
    public void A_new_aggregate_has_no_events_and_version_zero()
    {
        var aggregate = new TestAggregate();

        Assert.Empty(aggregate.GetDomainEvents());
        Assert.Equal(0u, aggregate.Version);
    }

    private readonly record struct TestId(Guid Value);

    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TestAggregate() : AggregateRoot<TestId>(new TestId(Guid.CreateVersion7()))
    {
        public void DoSomething(DateTimeOffset now) => Raise(new SomethingHappened(now));
    }
}
