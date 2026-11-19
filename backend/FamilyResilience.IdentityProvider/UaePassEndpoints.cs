using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The optional upstream sign-in with a UAE PASS <b>staging</b> test account (D-96). <see cref="Start"/> remembers the
/// validated authorization request under a new random state and sends the browser to UAE PASS;
/// <c>GET /uaepass/callback</c> takes that request back (single use), exchanges the code on the back channel, reads
/// userinfo, and finishes exactly like the persona page: a single-use code, the client's state and <c>iss</c>. Failures
/// go back to the client as <c>access_denied</c> (the person refused) or <c>temporarily_unavailable</c> (anything else),
/// with fixed descriptions; the log names a fixed reason code only.
/// </summary>
internal static class UaePassEndpoints
{
    public const string Disabled = "UAE PASS (staging) sign-in is not enabled in this identity provider.";
    public const string Unavailable = "UAE PASS (staging) sign-in could not be completed. Try again.";
    public const string Refused = "The UAE PASS (staging) sign-in was cancelled or refused.";
    public const string UnknownState = "This UAE PASS sign-in is unknown, expired or already finished. Start again from the app.";

    public static void MapUaePass(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(UaePassOptions.CallbackPath, CallbackAsync);
        endpoints.MapGet(UaePassOptions.SignedOutPath, SignedOut);
    }

    /// <summary>
    /// Sends the browser to UAE PASS logout, to come back to <see cref="SignedOut"/> with a single-use key, and then on to
    /// the client's (already validated) post-logout redirect URI with its state. Null when the pending store is full:
    /// the caller then redirects to the client directly.
    /// </summary>
    public static IResult? StartSignOut(
        string postLogoutRedirectUri, string? state, PendingSignOuts pending, IdentityProviderOptions options)
    {
        var key = pending.Add(postLogoutRedirectUri, state);
        if (key is null)
        {
            return null;
        }

        var returnUri = QueryHelpers.AddQueryString(UaePassOptions.SignedOutUri(options.Issuer), "r", key);
        return Results.Redirect(QueryHelpers.AddQueryString(options.UaePass.LogoutEndpoint, "redirect_uri", returnUri));
    }

    /// <summary><c>GET /uaepass/signed-out?r=…</c>: back from UAE PASS logout. An unknown, expired or reused key shows the signed-out page.</summary>
    private static IResult SignedOut(HttpContext context, PendingSignOuts pending, Pages pages)
    {
        var query = context.Request.Query;
        if (query.Any(parameter => parameter.Value.Count > 1)
            || AuthorizationRequestValidator.Value(query["r"]) is not { } key
            || pending.Take(key) is not { } target)
        {
            return pages.SignedOut();
        }

        return Results.Redirect(target.State is null ? target.Uri : QueryHelpers.AddQueryString(target.Uri, "state", target.State));
    }

    public static IResult Start(
        AuthorizationRequest request, PendingUpstreamSignIns pending, IdentityProviderOptions options, ILoggerFactory loggers)
    {
        var uaePass = options.UaePass;
        if (!uaePass.IsActive)
        {
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Disabled, options.Issuer);
        }

        var state = pending.Add(request, PendingUpstreamSignIns.UaePass);
        if (state is null)
        {
            IdentityProviderLog.UaePassFailed(IdentityProviderLog.For(loggers), "too_many_pending", request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, options.Issuer);
        }

        KeyValuePair<string, string?>[] parameters =
        [
            new("response_type", "code"),
            new("client_id", uaePass.ClientId),
            new("redirect_uri", UaePassOptions.CallbackUri(options.Issuer)),
            new("scope", uaePass.Scope),
            new("state", state),
            new("acr_values", uaePass.AcrValues),
            new("ui_locales", UaePassLocale(request.UiLocales)),
        ];
        return Results.Redirect(QueryHelpers.AddQueryString(uaePass.AuthorizationEndpoint, parameters));
    }

    /// <summary>UAE PASS shows English or Arabic: Arabic when the first requested locale is Arabic, otherwise English.</summary>
    public static string UaePassLocale(string? uiLocales)
    {
        var first = uiLocales?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var language = first?.Split('-')[0];
        return string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        PendingUpstreamSignIns pending,
        UaePassClient uaePass,
        GrantStore grants,
        TimeProvider time,
        Pages pages,
        IOptions<IdentityProviderOptions> options,
        ILoggerFactory loggers)
    {
        var query = context.Request.Query;
        if (query.Any(parameter => parameter.Value.Count > 1)
            || AuthorizationRequestValidator.Value(query["state"]) is not { } state
            || pending.Take(state, PendingUpstreamSignIns.UaePass) is not { Request: var request })
        {
            return pages.Refused(UnknownState);
        }

        var settings = options.Value;
        var logger = IdentityProviderLog.For(loggers);
        if (AuthorizationRequestValidator.Value(query["error"]) is not null)
        {
            IdentityProviderLog.UaePassFailed(logger, "upstream_refused", request.Client.Id);
            return AuthorizationRedirects.Error(request, "access_denied", Refused, settings.Issuer);
        }

        if (!settings.UaePass.IsActive || AuthorizationRequestValidator.Value(query["code"]) is not { } upstreamCode)
        {
            IdentityProviderLog.UaePassFailed(logger, "callback_without_code", request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, settings.Issuer);
        }

        var outcome = await uaePass.SignInAsync(upstreamCode, context.RequestAborted);
        if (outcome.Identity is not { } identity)
        {
            IdentityProviderLog.UaePassFailed(logger, outcome.FailureReason!, request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, settings.Issuer);
        }

        var persona = identity.ToPersona();
        var code = grants.IssueCode(new AuthorizationGrant(
            request.Client.Id,
            request.RedirectUri,
            request.CodeChallenge,
            request.Nonce,
            request.GrantedScope,
            persona,
            time.GetUtcNow(),
            identity.Profile));
        IdentityProviderLog.SignedIn(logger, persona.Code, request.Client.Id);
        return AuthorizationRedirects.Code(request.RedirectUri, code, request.State, settings.Issuer);
    }
}
