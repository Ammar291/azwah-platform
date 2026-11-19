using System.Text.Json.Nodes;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// <c>POST /token</c>: the authorization code grant with PKCE and the exact redirect URI, and the refresh token grant
/// with rotation (RFC 6749 sections 4.1.3 and 6, RFC 7636, OAuth 2.0 Security BCP refresh token rotation).
/// </summary>
internal static class TokenEndpoint
{
    public static void MapToken(this IEndpointRouteBuilder endpoints) => endpoints.MapPost("/token", HandleAsync);

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ClientAuthenticator authenticator,
        GrantStore grants,
        TokenIssuer issuer,
        ILoggerFactory loggers)
    {
        var form = await FormRequests.ReadAsync(context.Request);
        if (form is null)
        {
            return OAuthJson.InvalidRequest("The body must be application/x-www-form-urlencoded.");
        }

        if (FormRequests.HasRepeatedFields(form))
        {
            return OAuthJson.InvalidRequest("A parameter appears more than once.");
        }

        var (client, error) = authenticator.Authenticate(context.Request, form);
        if (client is null)
        {
            return error!;
        }

        var logger = IdentityProviderLog.For(loggers);
        return AuthorizationRequestValidator.Value(form["grant_type"]) switch
        {
            "authorization_code" => ExchangeCode(form, client, grants, issuer, logger),
            "refresh_token" => Refresh(form, client, grants, issuer, logger),
            null => OAuthJson.InvalidRequest("grant_type is required."),
            _ => OAuthJson.Error("unsupported_grant_type", "Only authorization_code and refresh_token are supported."),
        };
    }

    private static OAuthJson ExchangeCode(
        IFormCollection form, RegisteredClient client, GrantStore grants, TokenIssuer issuer, ILogger logger)
    {
        var code = AuthorizationRequestValidator.Value(form["code"]);
        var redirectUri = AuthorizationRequestValidator.Value(form["redirect_uri"]);
        var verifier = AuthorizationRequestValidator.Value(form["code_verifier"]);
        if (code is null || redirectUri is null || verifier is null)
        {
            return OAuthJson.InvalidRequest("code, redirect_uri and code_verifier are required.");
        }

        var redemption = grants.RedeemCode(code);
        switch (redemption.Outcome)
        {
            case GrantOutcome.Reused:
                IdentityProviderLog.CodeReused(logger, client.Id);
                return OAuthJson.InvalidGrant("The code has already been used.");
            case not GrantOutcome.Accepted:
                return OAuthJson.InvalidGrant("The code is unknown or expired.");
        }

        var grant = redemption.Grant!;
        if (!string.Equals(grant.ClientId, client.Id, StringComparison.Ordinal)
            || !string.Equals(grant.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !Pkce.Matches(verifier, grant.CodeChallenge))
        {
            return OAuthJson.InvalidGrant("The code was issued to another client or redirect URI, or the code verifier does not match.");
        }

        if (!grants.TryIssueForCode(redemption.CodeKey!, out var refreshToken))
        {
            return OAuthJson.InvalidGrant("The code has already been used.");
        }

        var tokens = TokenResponse(issuer, issuer.CreateAccessToken(grant.Persona, client.Id, grant.Scope), grant.Scope, refreshToken);
        tokens["id_token"] = issuer.CreateIdToken(grant);
        return OAuthJson.Ok(tokens);
    }

    private static OAuthJson Refresh(
        IFormCollection form, RegisteredClient client, GrantStore grants, TokenIssuer issuer, ILogger logger)
    {
        var refreshToken = AuthorizationRequestValidator.Value(form["refresh_token"]);
        if (refreshToken is null)
        {
            return OAuthJson.InvalidRequest("refresh_token is required.");
        }

        var requestedScope = AuthorizationRequestValidator.Value(form["scope"])?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rotation = grants.Rotate(refreshToken, client.Id, requestedScope);
        switch (rotation.Outcome)
        {
            case GrantOutcome.Reused:
                IdentityProviderLog.RefreshTokenReused(logger, client.Id);
                return OAuthJson.InvalidGrant("The refresh token has already been used.");
            case GrantOutcome.ScopeExceeded:
                return OAuthJson.Error("invalid_scope", "A refresh cannot add scope.");
            case not GrantOutcome.Accepted:
                return OAuthJson.InvalidGrant("The refresh token is unknown, expired or revoked.");
        }

        IdentityProviderLog.Refreshed(logger, rotation.Grant!.Persona.Code, client.Id);
        // A new ID token (no nonce, the original auth_time: OpenID Connect Core 12.2), so an app that keeps only the
        // refresh token across restarts gets the sign-in's profile back.
        var tokens = TokenResponse(
            issuer, issuer.CreateAccessToken(rotation.Grant.Persona, client.Id, rotation.Scope!), rotation.Scope!, rotation.RefreshToken);
        if (rotation.Scope!.Split(' ').Contains("openid", StringComparer.Ordinal))
        {
            tokens["id_token"] = issuer.CreateIdToken(rotation.Grant, forRefresh: true);
        }

        return OAuthJson.Ok(tokens);
    }

    private static JsonObject TokenResponse(TokenIssuer issuer, string accessToken, string scope, string? refreshToken)
    {
        var tokens = new JsonObject
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = issuer.AccessTokenSeconds,
            ["scope"] = scope,
        };
        if (refreshToken is not null)
        {
            tokens["refresh_token"] = refreshToken;
        }

        return tokens;
    }
}
