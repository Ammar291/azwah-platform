namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The outcome of checking an authorization request. Until the client and its redirect URI are known to be
/// registered, a problem is shown on a page (<see cref="PageError"/>) and the browser is never redirected; after that,
/// problems go back to the client as an error redirect (<see cref="RedirectError"/>).
/// </summary>
internal sealed record AuthorizationValidation(
    AuthorizationRequest? Request,
    string? PageError,
    RedirectError? RedirectError)
{
    public static AuthorizationValidation Valid(AuthorizationRequest request) => new(request, null, null);

    public static AuthorizationValidation Page(string message) => new(null, message, null);

    public static AuthorizationValidation Redirect(string redirectUri, string? state, string error, string description) =>
        new(null, null, new RedirectError(redirectUri, state, error, description));
}
