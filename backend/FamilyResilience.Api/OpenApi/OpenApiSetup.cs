namespace FamilyResilience.Api.OpenApi;

public static class OpenApiSetup
{
    /// <summary>
    /// The OpenAPI document (served in Development only): the Bearer security scheme, the authorization each operation
    /// needs, the 429 every operation may answer, and merge-patch fields documented as the values they carry.
    /// </summary>
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<AuthorizationOperationTransformer>();
            options.AddOperationTransformer<IdempotencyKeyOperationTransformer>();
            options.AddOperationTransformer<RateLimitOperationTransformer>();
            options.AddSchemaTransformer<OptionalSchemaTransformer>();
        });
}
