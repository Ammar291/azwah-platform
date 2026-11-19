using FamilyResilience.Application.Abstractions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FamilyResilience.Api.OpenApi;

/// <summary>
/// Documents a merge-patch field (<see cref="Optional{T}"/>) as the value it carries; its custom JSON converter would
/// otherwise leave the schema empty.
/// </summary>
internal sealed class OptionalSchemaTransformer : IOpenApiSchemaTransformer
{
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var type = context.JsonTypeInfo.Type;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Optional<>))
        {
            return;
        }

        var valueType = type.GetGenericArguments()[0];
        var inner = await context.GetOrCreateSchemaAsync(valueType, null, cancellationToken);
        var clearable = Nullable.GetUnderlyingType(valueType) is not null;
        schema.OneOf = clearable ? [inner, new OpenApiSchema { Type = JsonSchemaType.Null }] : [inner];
        schema.Description = clearable
            ? "Merge patch: leave the property out to keep the current value; send null to clear it."
            : "Merge patch: leave the property out to keep the current value.";
    }
}
