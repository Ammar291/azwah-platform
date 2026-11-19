using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The optional upstream sign-in with a Google account (D-147), built like the UAE PASS one. <see cref="Start"/>
/// remembers the validated authorization request under a new random state with a PKCE verifier and sends the browser to
/// Google, asking it to show the account chooser (a shared family phone); <c>GET /google/callback</c> takes that request
/// back (single use, Google's only), exchanges the code on the back channel, reads userinfo, and finishes exactly like
/// the persona page: a single-use code, the client's state and <c>iss</c>. Failures go back to the client as
/// <c>access_denied</c> (the person refused) or <c>temporarily_unavailable</c> (anything else), with fixed descriptions;
/// the log names a fixed reason code only. In Development, a client that opts in to Google staff sign-in gets a staff
/// account instead of a participant (<see cref="GoogleOptions.StaffRolesFor"/>, D-149).
/// </summary>
internal static class GoogleEndpoints
{
    public const string Disabled = "Google sign-in is not enabled in this identity provider.";
    public const string Unavailable = "Google sign-in could not be completed. Try again.";
    public const string Refused = "The Google sign-in was cancelled or refused.";
    public const string UnknownState = "This Google sign-in is unknown, expired or already finished. Start again from the app.";

    public static void MapGoogle(this IEndpointRouteBuilder endpoints) => endpoints.MapGet(GoogleOptions.CallbackPath, CallbackAsync);

    public static IResult Start(
        AuthorizationRequest request, PendingUpstreamSignIns pending, IdentityProviderOptions options, ILoggerFactory loggers)
    {
        var google = options.Google;
        if (!google.IsActive)
        {
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Disabled, options.Issuer);
        }

        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var state = pending.Add(request, PendingUpstreamSignIns.Google, verifier);
        if (state is null)
        {
            IdentityProviderLog.GoogleFailed(IdentityProviderLog.For(loggers), "too_many_pending", request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, options.Issuer);
        }

        KeyValuePair<string, string?>[] parameters =
        [
            new("response_type", "code"),
            new("client_id", google.ClientId),
            new("redirect_uri", GoogleOptions.CallbackUri(options.Issuer)),
            new("scope", google.Scope),
            new("state", state),
            new("code_challenge", Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
            new("code_challenge_method", "S256"),
            new("prompt", "select_account"),
            new("hl", UaePassEndpoints.UaePassLocale(request.UiLocales)),
        ];
        return Results.Redirect(QueryHelpers.AddQueryString(google.AuthorizationEndpoint, parameters));
    }

    private static async Task<IResult> CallbackAsync(
        HttpContext context,
        PendingUpstreamSignIns pending,
        GoogleClient google,
        GrantStore grants,
        TimeProvider time,
        Pages pages,
        ProviderMode mode,
        IOptions<IdentityProviderOptions> options,
        ILoggerFactory loggers)
    {
        var query = context.Request.Query;
        if (query.Any(parameter => parameter.Value.Count > 1)
            || AuthorizationRequestValidator.Value(query["state"]) is not { } state
            || pending.Take(state, PendingUpstreamSignIns.Google) is not { Request: var request, CodeVerifier: { } verifier })
        {
            return pages.Refused(UnknownState);
        }

        var settings = options.Value;
        var logger = IdentityProviderLog.For(loggers);
        if (AuthorizationRequestValidator.Value(query["error"]) is not null)
        {
            IdentityProviderLog.GoogleFailed(logger, "upstream_refused", request.Client.Id);
            return AuthorizationRedirects.Error(request, "access_denied", Refused, settings.Issuer);
        }

        if (!settings.Google.IsActive || AuthorizationRequestValidator.Value(query["code"]) is not { } upstreamCode)
        {
            IdentityProviderLog.GoogleFailed(logger, "callback_without_code", request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, settings.Issuer);
        }

        var outcome = await google.SignInAsync(upstreamCode, verifier, context.RequestAborted);
        if (outcome.Identity is not { } identity)
        {
            IdentityProviderLog.GoogleFailed(logger, outcome.FailureReason!, request.Client.Id);
            return AuthorizationRedirects.Error(request, "temporarily_unavailable", Unavailable, settings.Issuer);
        }

        var persona = identity.ToPersona(settings.Google.StaffRolesFor(request.Client, mode));
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
