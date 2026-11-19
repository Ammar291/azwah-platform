using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// <c>/.well-known/openid-configuration</c>, <c>/jwks</c> and a home page. In Development, endpoint URLs follow the
/// request's own scheme and host (so the API in Docker Compose can reach http://identity-provider:5090 while browsers
/// use localhost); deployed, they come from the issuer. The <c>issuer</c> itself never follows the request.
/// </summary>
internal static class DiscoveryEndpoints
{
    public static void MapDiscovery(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/openid-configuration", Discovery);
        endpoints.MapGet("/jwks", (SigningKey key) => OAuthJson.Ok(new JsonObject { ["keys"] = new JsonArray(key.PublicJwk()) }));
        endpoints.MapGet("/", (Pages pages, IOptions<IdentityProviderOptions> options) => pages.Home(options.Value.Issuer, options.Value.UaePass, options.Value.Google));
    }

    private static OAuthJson Discovery(HttpContext context, ProviderMode mode, IOptions<IdentityProviderOptions> options)
    {
        var request = context.Request;
        var origin = mode.IsDeployed
            ? options.Value.Issuer.TrimEnd('/')
            : $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}";
        string[] authenticationMethods = ["client_secret_basic", "client_secret_post", "none"];
        var metadata = new JsonObject
        {
            ["issuer"] = options.Value.Issuer,
            ["authorization_endpoint"] = $"{origin}/authorize",
            ["token_endpoint"] = $"{origin}/token",
            ["jwks_uri"] = $"{origin}/jwks",
            ["revocation_endpoint"] = $"{origin}/revoke",
            ["end_session_endpoint"] = $"{origin}/end-session",
            ["response_types_supported"] = Array("code"),
            ["response_modes_supported"] = Array("query"),
            ["grant_types_supported"] = Array("authorization_code", "refresh_token"),
            ["subject_types_supported"] = Array("public"),
            ["id_token_signing_alg_values_supported"] = Array(SigningKey.Algorithm),
            ["scopes_supported"] = Array("openid", AuthorizationGrant.OfflineAccess),
            ["claims_supported"] = options.Value.UaePass.IsActive || options.Value.Google.IsActive
                ? Array("iss", "sub", "aud", "azp", "exp", "iat", "auth_time", "nonce", "name", "given_name", "family_name", "gender", "azwah_profile")
                : Array("iss", "sub", "aud", "azp", "exp", "iat", "auth_time", "nonce"),
            ["prompt_values_supported"] = Array("login", "none"),
            ["token_endpoint_auth_methods_supported"] = Array(authenticationMethods),
            ["revocation_endpoint_auth_methods_supported"] = Array(authenticationMethods),
            ["code_challenge_methods_supported"] = Array("S256"),
            ["authorization_response_iss_parameter_supported"] = true,
            // The idp_hint values that lead to a configured upstream sign-in, so an app offers only what works (D-147).
            ["azwah_identity_providers"] = Array(UpstreamHints(options.Value)),
        };
        if (mode.DevelopmentMarkers)
        {
            // Marks the provider as simulated: release builds of the mobile app refuse such an issuer.
            metadata["azwah_development_only"] = true;
        }

        return OAuthJson.Ok(metadata);
    }

    private static string[] UpstreamHints(IdentityProviderOptions options)
    {
        var hints = new List<string>();
        if (options.UaePass.IsActive)
        {
            hints.Add(AuthorizationRequest.UaePassHint);
        }

        if (options.Google.IsActive)
        {
            hints.Add(AuthorizationRequest.GoogleHint);
        }

        return [.. hints];
    }

    private static JsonArray Array(params string[] values) => new([.. values.Select(value => (JsonNode?)JsonValue.Create(value))]);
}
