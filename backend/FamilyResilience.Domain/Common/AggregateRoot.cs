namespace FamilyResilience.Domain.Common;

/// <summary>
/// The entry point of a consistency boundary. Only aggregate roots are loaded and saved through repositories, raise
/// domain events and carry a concurrency token.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct, IEquatable<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>Optimistic concurrency token. Persistence maps it to PostgreSQL's <c>xmin</c> system column.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<IDomainEvent> GetDomainEvents() => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
