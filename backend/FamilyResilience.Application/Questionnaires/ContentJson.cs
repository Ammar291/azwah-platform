using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>JSON options for content files: camelCase, snake-case enum names, unknown members and missing values refused.</summary>
public static class ContentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
    };
}
