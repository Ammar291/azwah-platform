namespace FamilyResilience.IdentityProvider;

/// <summary>One registered client, under "IdentityProvider:Clients:{clientId}".</summary>
public sealed class IdentityProviderClientOptions
{
    /// <summary>
    /// A confidential client authenticates with <see cref="Secret"/> (client_secret_basic or client_secret_post); a
    /// public client sends only its ID and relies on PKCE.
    /// </summary>
    public bool Confidential { get; set; }

    /// <summary>
    /// The confidential client's secret. It has no default: while it is missing, every token and revocation request
    /// from the client fails with invalid_client.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>Redirect URIs, matched exactly.</summary>
    public List<string> RedirectUris { get; } = [];

    /// <summary>Post-logout redirect URIs, matched exactly.</summary>
    public List<string> PostLogoutRedirectUris { get; } = [];

    /// <summary>
    /// Also accept Expo Go addresses: <c>exp://…/--/auth/callback</c> to sign in and <c>exp://…/--/auth/signed-out</c>
    /// after signing out (their host is the developer's computer, so it cannot be registered in advance).
    /// </summary>
    public bool AllowExpoGoRedirects { get; set; }

    /// <summary>
    /// Google staff sign-in (D-149), Development only and for a confidential client: a Google sign-in for this client gets
    /// the subject <c>google-staff|{sub}</c> and <see cref="GoogleOptions.StaffRoles"/> instead of <c>google|{sub}</c> and
    /// <c>participant</c>. Off while <see cref="GoogleOptions.StaffRoles"/> is empty.
    /// </summary>
    public bool GoogleStaffSignIn { get; set; }
}
