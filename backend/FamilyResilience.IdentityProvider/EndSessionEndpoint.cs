using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// <c>GET /end-session</c> (OpenID Connect RP-Initiated Logout). The provider keeps no session, so this only sends the
/// browser back, and only to a post-logout redirect URI registered for the client named by <c>client_id</c> or by the
/// <c>id_token_hint</c> it signed. Anything else shows a page and never redirects. With <c>idp_hint=uae_pass</c> and
/// UAE PASS active, the browser first ends its UAE PASS session (<see cref="UaePassEndpoints.StartSignOut"/>), so a
/// shared phone asks again next time (D-129).
/// </summary>
internal static class EndSessionEndpoint
{
    public static void MapEndSession(this IEndpointRouteBuilder endpoints) => endpoints.MapGet("/end-session", Handle);

    private static IResult Handle(
        HttpContext context,
        ClientRegistry clients,
        SigningKey key,
        Pages pages,
        PendingSignOuts pendingSignOuts,
        IOptions<IdentityProviderOptions> options,
        ILoggerFactory loggers)
    {
        var query = context.Request.Query;
        if (query.Any(parameter => parameter.Value.Count > 1))
        {
            return pages.Refused("A parameter appears more than once.");
        }

        var clientId = AuthorizationRequestValidator.Value(query["client_id"]);
        var client = clients.Find(clientId);
        if (clientId is not null && client is null)
        {
            return pages.Refused("The request does not name a registered client.");
        }

        if (AuthorizationRequestValidator.Value(query["id_token_hint"]) is { } hint)
        {
            var hinted = HintedClient(hint, key, clients, options.Value.Issuer);
            if (hinted is null || (client is not null && client.Id != hinted.Id))
            {
                return pages.Refused("The ID token hint was not issued by this provider to this client.");
            }

            client = hinted;
        }

        var postLogoutRedirectUri = AuthorizationRequestValidator.Value(query["post_logout_redirect_uri"]);
        if (postLogoutRedirectUri is null)
        {
            return pages.SignedOut();
        }

        if (client is null || !client.HasPostLogoutRedirectUri(postLogoutRedirectUri))
        {
            return pages.Refused("The post-logout address is not registered for this client.");
        }

        var logger = IdentityProviderLog.For(loggers);
        IdentityProviderLog.SignedOut(logger, client.Id);
        var state = AuthorizationRequestValidator.Value(query["state"]);
        var settings = options.Value;
        if (settings.UaePass.IsActive
            && AuthorizationRequestValidator.Value(query["idp_hint"]) == AuthorizationRequest.UaePassHint
            && UaePassEndpoints.StartSignOut(postLogoutRedirectUri, state, pendingSignOuts, settings) is { } upstream)
        {
            IdentityProviderLog.UaePassSignedOut(logger, client.Id);
            return upstream;
        }

        return Results.Redirect(state is null ? postLogoutRedirectUri : QueryHelpers.AddQueryString(postLogoutRedirectUri, "state", state));
    }

    /// <summary>The client an ID token of ours was issued to. An expired hint still identifies it.</summary>
    private static RegisteredClient? HintedClient(string hint, SigningKey key, ClientRegistry clients, string issuer)
    {
        var claims = key.ReadOwnJwt(hint);
        if (claims is null || Text(claims["iss"]) != issuer)
        {
            return null;
        }

        return clients.Find(Text(claims["aud"]));
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
