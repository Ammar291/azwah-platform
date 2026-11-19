using System.Text.Json.Nodes;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// JSON answers of the token and revocation endpoints (RFC 6749 section 5): never cached, errors as
/// <c>{"error": "...", "error_description": "..."}</c> with fixed descriptions that never echo the request.
/// </summary>
internal sealed class OAuthJson(int statusCode, JsonObject? body, string? challenge = null) : IResult
{
    public static OAuthJson Ok(JsonObject body) => new(StatusCodes.Status200OK, body);

    public static OAuthJson Empty() => new(StatusCodes.Status200OK, null);

    public static OAuthJson Error(string error, string description) =>
        new(StatusCodes.Status400BadRequest, ErrorBody(error, description));

    public static OAuthJson ServerError() =>
        new(StatusCodes.Status500InternalServerError, ErrorBody("server_error", "The identity provider failed."));

    public static OAuthJson InvalidRequest(string description) => Error("invalid_request", description);

    public static OAuthJson InvalidGrant(string description) => Error("invalid_grant", description);

    /// <param name="usedBasic">The client tried the Authorization header, so the answer names the Basic scheme (RFC 6749 5.2).</param>
    public static OAuthJson InvalidClient(bool usedBasic) => new(
        StatusCodes.Status401Unauthorized,
        ErrorBody("invalid_client", "Client authentication failed."),
        usedBasic ? "Basic realm=\"azwah-development-identity\"" : null);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        response.StatusCode = statusCode;
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        if (challenge is not null)
        {
            response.Headers.WWWAuthenticate = challenge;
        }

        if (body is not null)
        {
            response.ContentType = "application/json; charset=utf-8";
            await response.WriteAsync(body.ToJsonString(), httpContext.RequestAborted);
        }
    }

    private static JsonObject ErrorBody(string error, string description) =>
        new() { ["error"] = error, ["error_description"] = description };
}
