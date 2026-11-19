using Microsoft.AspNetCore.WebUtilities;

namespace FamilyResilience.IdentityProvider;

/// <summary>Redirects back to a validated client redirect URI, always with <c>iss</c> (RFC 9207).</summary>
internal static class AuthorizationRedirects
{
    public static IResult Code(string redirectUri, string code, string state, string issuer) =>
        Redirect(redirectUri, [new("code", code), new("state", state), new("iss", issuer)]);

    public static IResult Error(RedirectError error, string issuer) => Redirect(error.RedirectUri,
    [
        new("error", error.Error),
        new("error_description", error.Description),
        new("state", error.State),
        new("iss", issuer),
    ]);

    public static IResult Error(AuthorizationRequest request, string error, string description, string issuer) =>
        Error(new RedirectError(request.RedirectUri, request.State, error, description), issuer);

    private static IResult Redirect(string redirectUri, KeyValuePair<string, string?>[] parameters) =>
        Results.Redirect(QueryHelpers.AddQueryString(redirectUri, parameters.Where(parameter => parameter.Value is not null)));
}
