using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// <c>GET /authorize</c> shows the persona picker for a valid request (Development only); <c>POST /authorize</c> checks
/// the request again and returns a single-use code (or <c>access_denied</c> on Cancel). Responses carry <c>iss</c>
/// (RFC 9207). Either one with <c>idp_hint=uae_pass</c> starts the UAE PASS staging sign-in instead
/// (<see cref="UaePassEndpoints"/>), and with <c>idp_hint=google</c> the Google sign-in (<see cref="GoogleEndpoints"/>,
/// D-147); deployed, every other valid request goes to UAE PASS and a persona choice is refused.
/// </summary>
internal static class AuthorizeEndpoints
{
    public static void MapAuthorize(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/authorize", Show);
        endpoints.MapPost("/authorize", ChooseAsync);
    }

    private static IResult Show(
        HttpContext context,
        ClientRegistry clients,
        PendingUpstreamSignIns pending,
        ProviderMode mode,
        Pages pages,
        IOptions<IdentityProviderOptions> options,
        ILoggerFactory loggers)
    {
        var validation = AuthorizationRequestValidator.Validate(name => context.Request.Query[name], clients);
        if (validation.Request is { } request)
        {
            if (request.AsksForGoogle)
            {
                return GoogleEndpoints.Start(request, pending, options.Value, loggers);
            }

            if (request.AsksForUaePass || !mode.PersonasAvailable)
            {
                return UaePassEndpoints.Start(request, pending, options.Value, loggers);
            }

            return pages.PersonaPicker(request, $"{context.Request.PathBase}/authorize", options.Value.UaePass, options.Value.Google);
        }

        return validation.RedirectError is { } error
            ? AuthorizationRedirects.Error(error, options.Value.Issuer)
            : pages.Refused(validation.PageError!);
    }

    private static async Task<IResult> ChooseAsync(
        HttpContext context,
        ClientRegistry clients,
        GrantStore grants,
        PendingUpstreamSignIns pending,
        TimeProvider time,
        ProviderMode mode,
        Pages pages,
        IOptions<IdentityProviderOptions> options,
        ILoggerFactory loggers)
    {
        var form = await FormRequests.ReadAsync(context.Request);
        if (form is null)
        {
            return pages.Refused("The sign-in form must be sent as a URL-encoded form.");
        }

        var issuer = options.Value.Issuer;
        var validation = AuthorizationRequestValidator.Validate(name => form[name], clients);
        if (validation.Request is not { } request)
        {
            return validation.RedirectError is { } error ? AuthorizationRedirects.Error(error, issuer) : pages.Refused(validation.PageError!);
        }

        if (AuthorizationRequestValidator.Value(form["cancel"]) is not null)
        {
            return AuthorizationRedirects.Error(request, "access_denied", "The sign-in was cancelled.", issuer);
        }

        if (request.AsksForGoogle)
        {
            return GoogleEndpoints.Start(request, pending, options.Value, loggers);
        }

        if (request.AsksForUaePass)
        {
            return UaePassEndpoints.Start(request, pending, options.Value, loggers);
        }

        if (!mode.PersonasAvailable)
        {
            return AuthorizationRedirects.Error(request, "invalid_request", "This provider signs people in with UAE PASS only.", issuer);
        }

        var persona = Personas.Find(AuthorizationRequestValidator.Value(form["persona"]));
        if (persona is null)
        {
            return AuthorizationRedirects.Error(request, "invalid_request", "Choose one of the listed personas.", issuer);
        }

        var code = grants.IssueCode(new AuthorizationGrant(
            request.Client.Id, request.RedirectUri, request.CodeChallenge, request.Nonce, request.GrantedScope, persona, time.GetUtcNow()));
        IdentityProviderLog.SignedIn(IdentityProviderLog.For(loggers), persona.Code, request.Client.Id);
        return AuthorizationRedirects.Code(request.RedirectUri, code, request.State, issuer);
    }
}
