using Microsoft.Extensions.Primitives;

namespace FamilyResilience.IdentityProvider;

/// <summary>Checks an authorization request, from the query string (GET) or the persona form (POST) alike.</summary>
internal static class AuthorizationRequestValidator
{
    private const int MaxStateOrNonceLength = 1024;

    // ui_locales and idp_hint are hints: longer values are ignored, not refused.
    private const int MaxHintLength = 64;

    private static readonly string[] SingleValued =
    [
        "client_id", "redirect_uri", "response_type", "response_mode", "scope", "state", "nonce", "code_challenge",
        "code_challenge_method", "prompt", "request", "request_uri", "max_age", "login_hint", "ui_locales", "persona",
        "cancel", "idp_hint",
    ];

    public static AuthorizationValidation Validate(Func<string, StringValues> parameters, ClientRegistry clients)
    {
        var client = clients.Find(Value(parameters("client_id")));
        if (client is null || parameters("client_id").Count != 1)
        {
            return AuthorizationValidation.Page("The request does not name a registered client.");
        }

        var redirectUri = Value(parameters("redirect_uri"));
        if (redirectUri is null || parameters("redirect_uri").Count != 1 || !client.HasRedirectUri(redirectUri))
        {
            return AuthorizationValidation.Page("The redirect address is not registered for this client.");
        }

        // From here on the redirect URI is trusted, so problems go back to the client.
        var state = Value(parameters("state"));
        var echoedState = state is { Length: <= MaxStateOrNonceLength } && parameters("state").Count == 1 ? state : null;
        AuthorizationValidation Fail(string error, string description) =>
            AuthorizationValidation.Redirect(redirectUri, echoedState, error, description);

        if (SingleValued.Any(name => parameters(name).Count > 1))
        {
            return Fail("invalid_request", "A parameter appears more than once.");
        }

        if (Value(parameters("request")) is not null)
        {
            return Fail("request_not_supported", "Request objects are not supported.");
        }

        if (Value(parameters("request_uri")) is not null)
        {
            return Fail("request_uri_not_supported", "Request URIs are not supported.");
        }

        if (Value(parameters("response_mode")) is { } responseMode && responseMode != "query")
        {
            return Fail("invalid_request", "Only response_mode=query is supported.");
        }

        var responseType = Value(parameters("response_type"));
        if (responseType is null)
        {
            return Fail("invalid_request", "response_type is required.");
        }

        if (responseType != "code")
        {
            return Fail("unsupported_response_type", "Only response_type=code is supported.");
        }

        var requestedScope = Value(parameters("scope")) ?? string.Empty;
        var scopes = requestedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains("openid", StringComparer.Ordinal))
        {
            return Fail("invalid_scope", "The scope must include openid.");
        }

        if (state is null)
        {
            return Fail("invalid_request", "state is required.");
        }

        if (echoedState is null)
        {
            return Fail("invalid_request", "state is too long.");
        }

        var nonce = Value(parameters("nonce"));
        if (nonce is not { Length: <= MaxStateOrNonceLength })
        {
            return Fail("invalid_request", "nonce is required and must be at most 1024 characters.");
        }

        if (Value(parameters("code_challenge_method")) != "S256")
        {
            return Fail("invalid_request", "PKCE with code_challenge_method=S256 is required.");
        }

        var challenge = Value(parameters("code_challenge"));
        if (!Pkce.IsS256Challenge(challenge))
        {
            return Fail("invalid_request", "code_challenge must be a base64url-encoded SHA-256 value.");
        }

        var prompts = Value(parameters("prompt"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (prompts.Contains("none", StringComparer.Ordinal))
        {
            return Fail("login_required", "This provider keeps no session: a persona must be chosen every time.");
        }

        var grantedScope = scopes.Contains(AuthorizationGrant.OfflineAccess, StringComparer.Ordinal)
            ? $"openid {AuthorizationGrant.OfflineAccess}"
            : "openid";
        var uiLocales = Value(parameters("ui_locales")) is { Length: <= MaxHintLength } locales ? locales : null;
        var idpHint = Value(parameters("idp_hint")) is { Length: <= MaxHintLength } hint ? hint : null;
        return AuthorizationValidation.Valid(new AuthorizationRequest(
            client, redirectUri, requestedScope, grantedScope, state, nonce, challenge!, uiLocales, idpHint));
    }

    /// <summary>The first value when it is not empty; empty parameters count as absent (RFC 6749 section 3.1).</summary>
    public static string? Value(StringValues values) => values.Count == 0 || string.IsNullOrEmpty(values[0]) ? null : values[0];
}
