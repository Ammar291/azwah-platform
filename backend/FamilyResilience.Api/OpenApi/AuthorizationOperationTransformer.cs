using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FamilyResilience.Api.OpenApi;

/// <summary>
/// Marks every operation that is not AllowAnonymous as needing the bearer token, and appends the policies it requires to
/// its description, so the contract shows who may call what.
/// </summary>
internal sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName, context.Document)] = [],
            },
        ];

        var policies = metadata.OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requirement = policies.Count == 0
            ? "Authorization: any signed-in caller."
            : $"Authorization: policy {string.Join(" and ", policies)}.";
        operation.Description = string.IsNullOrEmpty(operation.Description) ? requirement : $"{operation.Description}\n\n{requirement}";
        return Task.CompletedTask;
    }
}
