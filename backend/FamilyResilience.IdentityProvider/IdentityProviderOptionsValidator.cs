using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Refuses to start with settings that would issue unusable tokens, and, outside Development (<see cref="ProviderMode"/>),
/// with anything a deployed provider must not have: no UAE PASS, a plain-http issuer, a random signing key, plain-http
/// or Expo Go redirect URIs, or a confidential client without a secret. Messages name settings, never values.
/// </summary>
internal sealed class IdentityProviderOptionsValidator(ProviderMode mode) : IValidateOptions<IdentityProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentityProviderOptions options)
    {
        var failures = new List<string>();
        if (mode.IsProduction)
        {
            failures.Add(
                $"The Production environment is refused while only UAE PASS staging ({UaePassOptions.StagingHost}) can be configured: production UAE PASS is not onboarded (D-129, open question 5). Deploy as Staging.");
        }

        if (!IsPlainHttpUrl(options.Issuer))
        {
            failures.Add("IdentityProvider:Issuer must be an absolute http or https URL without user info, query or fragment.");
        }
        else if (mode.IsDeployed && !IsHttps(options.Issuer))
        {
            failures.Add("IdentityProvider:Issuer must be https outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("IdentityProvider:Audience is required.");
        }

        if (options.AccessTokenSeconds is < 60 or > 86_400)
        {
            failures.Add("IdentityProvider:AccessTokenSeconds must be between 60 and 86400.");
        }

        if (options.Clients.Count == 0)
        {
            failures.Add("IdentityProvider:Clients must register at least one client.");
        }

        foreach (var (clientId, client) in options.Clients)
        {
            ValidateClient(clientId, client, failures);
        }

        ValidateSigningKey(options.SigningKey, failures);
        ValidateUaePass(options.UaePass, failures);
        ValidateGoogle(options.Google, failures);
        ValidateGoogleStaffSignIn(options, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateClient(string clientId, IdentityProviderClientOptions client, List<string> failures)
    {
        if (client.RedirectUris.Count == 0 && !client.AllowExpoGoRedirects)
        {
            failures.Add($"IdentityProvider:Clients:{clientId}:RedirectUris must list at least one URI.");
        }

        var uris = client.RedirectUris.Concat(client.PostLogoutRedirectUris).ToList();
        if (!uris.All(IsRegistrableRedirect))
        {
            failures.Add($"IdentityProvider:Clients:{clientId} has a redirect URI that is not absolute or has a fragment.");
        }

        if (!mode.IsDeployed)
        {
            return;
        }

        if (client.AllowExpoGoRedirects || !uris.All(IsDeployableRedirect))
        {
            failures.Add(
                $"IdentityProvider:Clients:{clientId} may register only https or custom-scheme redirect URIs outside Development, and AllowExpoGoRedirects must be false.");
        }

        if (client.Confidential && string.IsNullOrEmpty(client.Secret))
        {
            failures.Add($"IdentityProvider:Clients:{clientId}:Secret is required for a confidential client outside Development.");
        }
    }

    private void ValidateSigningKey(SigningKeyOptions? signingKey, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(signingKey?.Pem))
        {
            if (mode.IsDeployed)
            {
                failures.Add("IdentityProvider:SigningKey:Pem is required outside Development (an RSA private key of at least 2048 bits).");
            }

            return;
        }

        if (SigningKey.Problem(signingKey.Pem) is not null)
        {
            failures.Add("IdentityProvider:SigningKey:Pem could not be read as an RSA private key of at least 2048 bits.");
        }
    }

    private void ValidateUaePass(UaePassOptions? uaePass, List<string> failures)
    {
        if (uaePass is not { Enabled: true })
        {
            if (mode.IsDeployed)
            {
                failures.Add("IdentityProvider:UaePass:Enabled must be true outside Development: UAE PASS is the only sign-in there.");
            }

            return;
        }

        // In Development, empty credentials leave UAE PASS off with a warning (UaePassOptions.IsActive); deployed, they
        // would leave the provider with no sign-in at all.
        if (mode.IsDeployed)
        {
            if (string.IsNullOrWhiteSpace(uaePass.ClientId))
            {
                failures.Add("IdentityProvider:UaePass:ClientId is required when UAE PASS sign-in is enabled.");
            }

            if (string.IsNullOrWhiteSpace(uaePass.ClientSecret))
            {
                failures.Add("IdentityProvider:UaePass:ClientSecret is required when UAE PASS sign-in is enabled.");
            }
        }

        foreach (var (setting, value) in new[]
                 {
                     ("AuthorizationEndpoint", uaePass.AuthorizationEndpoint),
                     ("TokenEndpoint", uaePass.TokenEndpoint),
                     ("UserInfoEndpoint", uaePass.UserInfoEndpoint),
                     ("LogoutEndpoint", uaePass.LogoutEndpoint),
                 })
        {
            if (!IsStagingEndpoint(value))
            {
                failures.Add(
                    $"IdentityProvider:UaePass:{setting} must be an https URL on {UaePassOptions.StagingHost}: only UAE PASS staging is allowed until production onboarding (D-129).");
            }
        }

        if (string.IsNullOrWhiteSpace(uaePass.Scope) || string.IsNullOrWhiteSpace(uaePass.AcrValues))
        {
            failures.Add("IdentityProvider:UaePass:Scope and IdentityProvider:UaePass:AcrValues are required when UAE PASS sign-in is enabled.");
        }
    }

    // Google sign-in is optional in both modes (D-147). Deployed, it must have its credentials and the owner's approval
    // reference: Google accounts are real people (open question 73).
    private void ValidateGoogle(GoogleOptions? google, List<string> failures)
    {
        if (google is not { Enabled: true })
        {
            return;
        }

        if (mode.IsDeployed)
        {
            if (string.IsNullOrWhiteSpace(google.ClientId))
            {
                failures.Add("IdentityProvider:Google:ClientId is required when Google sign-in is enabled.");
            }

            if (string.IsNullOrWhiteSpace(google.ClientSecret))
            {
                failures.Add("IdentityProvider:Google:ClientSecret is required when Google sign-in is enabled.");
            }

            if (string.IsNullOrWhiteSpace(google.ApprovalReference))
            {
                failures.Add(
                    "IdentityProvider:Google:ApprovalReference is required outside Development: Google accounts are real people (D-147, open question 73).");
            }
        }

        foreach (var (setting, value, host) in new[]
                 {
                     ("AuthorizationEndpoint", google.AuthorizationEndpoint, GoogleOptions.AuthorizationHost),
                     ("TokenEndpoint", google.TokenEndpoint, GoogleOptions.TokenHost),
                     ("UserInfoEndpoint", google.UserInfoEndpoint, GoogleOptions.UserInfoHost),
                 })
        {
            if (!IsHttpsEndpointOn(value, host))
            {
                failures.Add($"IdentityProvider:Google:{setting} must be an https URL on {host}.");
            }
        }

        if (string.IsNullOrWhiteSpace(google.Scope)
            || !google.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid", StringComparer.Ordinal))
        {
            failures.Add("IdentityProvider:Google:Scope is required and must include openid when Google sign-in is enabled.");
        }
    }

    // Google staff sign-in is for the demonstration stack only (D-149): Development, a confidential client that opts in,
    // and roles the platform knows. participant is refused: a staff client's Google sign-in is never a family account.
    private void ValidateGoogleStaffSignIn(IdentityProviderOptions options, List<string> failures)
    {
        var staffRoles = options.Google?.StaffRoles ?? [];
        var optedIn = options.Clients.Where(entry => entry.Value.GoogleStaffSignIn).ToList();
        if (mode.IsDeployed)
        {
            if (staffRoles.Count > 0)
            {
                failures.Add(
                    "IdentityProvider:Google:StaffRoles must be empty outside Development: Google staff sign-in is for the demonstration stack only (D-149).");
            }

            foreach (var (clientId, _) in optedIn)
            {
                failures.Add($"IdentityProvider:Clients:{clientId}:GoogleStaffSignIn must be false outside Development (D-149).");
            }

            return;
        }

        for (var index = 0; index < staffRoles.Count; index++)
        {
            var role = staffRoles[index];
            if (string.Equals(role, "participant", StringComparison.Ordinal))
            {
                failures.Add(
                    $"IdentityProvider:Google:StaffRoles:{index} must not be participant: Google staff sign-in gives staff roles only (D-149).");
            }
            else if (!GoogleOptions.AssignableStaffRoles.Contains(role, StringComparer.Ordinal))
            {
                failures.Add(
                    $"IdentityProvider:Google:StaffRoles:{index} must be one of {string.Join(", ", GoogleOptions.AssignableStaffRoles)}.");
            }
        }

        foreach (var (clientId, _) in optedIn.Where(entry => !entry.Value.Confidential))
        {
            failures.Add(
                $"IdentityProvider:Clients:{clientId}:GoogleStaffSignIn needs a confidential client: staff sign-in is for the admin dashboard, never the phone (D-149).");
        }
    }

    private static bool IsHttpsEndpointOn(string? value, string host) =>
        IsPlainHttpUrl(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.IdnHost, host, StringComparison.Ordinal);

    // Exactly the staging host on the default https port: never id.uaepass.ae (real people) or anything else.
    private static bool IsStagingEndpoint(string? value) =>
        IsPlainHttpUrl(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.IdnHost, UaePassOptions.StagingHost, StringComparison.Ordinal);

    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsPlainHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && !value!.Contains('?', StringComparison.Ordinal)
        && !value.Contains('#', StringComparison.Ordinal);

    // RFC 6749 section 3.1.2: an absolute URI without a fragment.
    private static bool IsRegistrableRedirect(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out _) && !value.Contains('#', StringComparison.Ordinal);

    // Deployed: https, or an app's own scheme (azwah://…); never plain http.
    private static bool IsDeployableRedirect(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeHttp;
}
