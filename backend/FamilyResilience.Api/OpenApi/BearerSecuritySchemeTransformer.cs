using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FamilyResilience.Api.OpenApi;

/// <summary>Declares the JWT bearer scheme every protected operation refers to.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "An access token from the configured OpenID Connect issuer. Roles are read from its role claims; "
                + "access to household data is decided from the platform's own records.",
        };
        return Task.CompletedTask;
    }
}
