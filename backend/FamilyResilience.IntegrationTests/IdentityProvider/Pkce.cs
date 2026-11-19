using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>A PKCE verifier and its S256 challenge (RFC 7636).</summary>
public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = IdentityProviderFlow.Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, IdentityProviderFlow.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }
}
