using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyResilience.Infrastructure.Persistence.Conventions;

/// <summary>Model-wide rules applied after every entity configuration, so no configuration can forget them.</summary>
internal static class ModelConventions
{
    public const int EnumColumnLength = 40;

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // While the model is being built, value objects can appear as key-less entity types; they never become tables.
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey is null)
            {
                continue;
            }

            foreach (var keyProperty in primaryKey.Properties)
            {
                keyProperty.ValueGenerated = ValueGenerated.Never;
            }

            if (IsAggregateRoot(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(AggregateRoot<Guid>.Version)).IsRowVersion();
            }

            StoreEnumsAsCheckedText(entityType);
        }
    }

    private static void StoreEnumsAsCheckedText(IMutableEntityType entityType)
    {
        var table = entityType.GetTableName();
        if (table is null)
        {
            return;
        }

        foreach (var property in entityType.GetProperties())
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!enumType.IsEnum)
            {
                continue;
            }

            if (property.GetProviderClrType() is null)
            {
                property.SetProviderClrType(typeof(string));
            }

            if (property.GetProviderClrType() != typeof(string))
            {
                throw new InvalidOperationException($"{entityType.DisplayName()}.{property.Name} must be stored as text.");
            }

            if (property.GetMaxLength() is null)
            {
                property.SetMaxLength(EnumColumnLength);
            }

            var column = property.GetColumnName();
            var allowed = string.Join(", ", Enum.GetNames(enumType).Select(name => $"'{name}'"));
            entityType.AddCheckConstraint($"ck_{table}_{column}", $"{column} IN ({allowed})");
        }
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }
}
