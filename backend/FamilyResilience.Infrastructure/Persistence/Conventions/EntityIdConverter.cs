using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FamilyResilience.Infrastructure.Persistence.Conventions;

/// <summary>Stores a strongly typed ID as its UUID.</summary>
public sealed class EntityIdConverter<TId>() : ValueConverter<TId, Guid>(id => id.Value, value => Create(value))
    where TId : struct, IEntityId<TId>
{
    // Expression trees cannot call a static abstract member directly, so the call goes through this method.
    private static TId Create(Guid value) => TId.From(value);
}
