using System.Text.Json;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>A token response from a finished sign-in, with the nonce the client sent.</summary>
public sealed record SignedIn(string Nonce, JsonElement Tokens)
{
    public string AccessToken => Tokens.GetProperty("access_token").GetString()!;

    public string IdToken => Tokens.GetProperty("id_token").GetString()!;

    public string RefreshToken => Tokens.GetProperty("refresh_token").GetString()!;
}
