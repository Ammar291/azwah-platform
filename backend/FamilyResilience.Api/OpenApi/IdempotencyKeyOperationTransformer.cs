using FamilyResilience.Api.Requests;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FamilyResilience.Api.OpenApi;

/// <summary>
/// Documents the <c>Idempotency-Key</c> header as the required UUID it is. The action binds it as an optional string so
/// that a missing or malformed key gets the API's own 400 keyed <c>idempotencyKey</c>.
/// </summary>
internal sealed class IdempotencyKeyOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.In == ParameterLocation.Header && parameter.Name == IdempotencyHeader.Name)
            {
                parameter.Required = true;
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
                parameter.Description = "A client-generated UUID. A retry with the same key returns the original outcome for 24 hours; "
                    + "the same key with a different request returns 409 requests.idempotency_key_reused.";
            }
        }

        return Task.CompletedTask;
    }
}
