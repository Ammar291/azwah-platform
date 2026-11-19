namespace FamilyResilience.Domain.Common;

public abstract class Entity<TId>
    where TId : struct, IEquatable<TId>
{
    protected Entity(TId id) => Id = id;

    public TId Id { get; private init; }
}
