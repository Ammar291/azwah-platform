using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// A JSON merge-patch field (RFC 7396): absent means "leave unchanged", present means "set to this value", and for a
/// nullable <typeparamref name="T"/> an explicit <c>null</c> means "clear". A <c>null</c> for a non-nullable
/// <typeparamref name="T"/> is malformed JSON and answers 400.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    public Optional(T value)
    {
        IsSet = true;
        Value = value;
    }

    /// <summary>Whether the request carried this field.</summary>
    public bool IsSet { get; }

    /// <summary>The value sent; meaningful only when <see cref="IsSet"/> is true.</summary>
    public T Value { get; }

    /// <summary>The value sent, or <paramref name="current"/> when the field was absent.</summary>
    public T Or(T current) => IsSet ? Value : current;
}

/// <summary>Reads <see cref="Optional{T}"/> so that a present <c>null</c> is kept apart from an absent field.</summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(
            typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        // Without this, System.Text.Json would turn a JSON null into default(Optional<T>), which reads as "absent".
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return new(JsonSerializer.Deserialize<T>(ref reader, options)!);
            }
            catch (JsonException)
            {
                // The nested call reports its own root ("$"); an exception without a path lets the serializer report this
                // property's path instead, for example "$.birthYear".
                throw new JsonException();
            }
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (value.IsSet)
            {
                JsonSerializer.Serialize(writer, value.Value, options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
