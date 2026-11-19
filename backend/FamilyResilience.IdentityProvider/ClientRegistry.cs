using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>The clients registered under "IdentityProvider:Clients", found by exact client ID.</summary>
internal sealed class ClientRegistry(IOptions<IdentityProviderOptions> options)
{
    public IEnumerable<RegisteredClient> All => options.Value.Clients.Select(entry => ToClient(entry.Key, entry.Value));

    public RegisteredClient? Find(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return null;
        }

        // Configuration keys are case-insensitive; client IDs are not.
        var match = options.Value.Clients.FirstOrDefault(entry => string.Equals(entry.Key, clientId, StringComparison.Ordinal));
        return match.Value is null ? null : ToClient(match.Key, match.Value);
    }

    private static RegisteredClient ToClient(string id, IdentityProviderClientOptions client) => new(
        id,
        client.Confidential,
        string.IsNullOrEmpty(client.Secret) ? null : client.Secret,
        [.. client.RedirectUris],
        [.. client.PostLogoutRedirectUris],
        client.AllowExpoGoRedirects,
        client.GoogleStaffSignIn);
}
