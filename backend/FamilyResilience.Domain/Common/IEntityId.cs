namespace FamilyResilience.Domain.Common;

/// <summary>
/// A strongly typed identifier over a UUID. Persistence maps every implementation to a <c>uuid</c> column by
/// convention, so a new ID type needs no mapping code.
/// </summary>
public interface IEntityId<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);
}
