using FamilyResilience.Domain;
using FamilyResilience.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Conventions;

/// <summary>Registers <see cref="EntityIdConverter{TId}"/> for every ID type in the domain, nullable ones included.</summary>
internal static class EntityIdConventions
{
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        foreach (var idType in DomainAssembly.Reference.GetTypes().Where(IsEntityId))
        {
            configurationBuilder.Properties(idType).HaveConversion(typeof(EntityIdConverter<>).MakeGenericType(idType));
        }
    }

    private static bool IsEntityId(Type type) =>
        type.IsValueType && type.GetInterfaces().Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEntityId<>));
}
