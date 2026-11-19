using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Infrastructure.Persistence.Outbox;

/// <summary>Writes strongly typed IDs as plain UUID strings.</summary>
public sealed class EntityIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IEntityId<>)
            && contract.GenericTypeArguments[0] == typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(typeof(EntityIdJsonConverter<>).MakeGenericType(typeToConvert))
        ?? throw new InvalidOperationException($"No JSON converter could be created for {typeToConvert.Name}.");

    private sealed class EntityIdJsonConverter<TId> : JsonConverter<TId>
        where TId : struct, IEntityId<TId>
    {
        public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TId.From(reader.GetGuid());

        public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
