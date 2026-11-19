using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FamilyResilience.Api.OpenApi;

/// <summary>
/// Every API operation can be refused by the global rate limit (and the sensitive ones by their own policy), so each
/// declares 429 with its <c>Retry-After</c> header.
/// </summary>
internal sealed class RateLimitOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        operation.Responses ??= [];
        operation.Responses["429"] = new OpenApiResponse
        {
            Description = "Too Many Requests: requests.rate_limited. Retry after the number of seconds in Retry-After.",
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds to wait before the next attempt.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                },
            },
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchemaReference("ProblemDetails", context.Document),
                },
            },
        };
        return Task.CompletedTask;
    }
}
