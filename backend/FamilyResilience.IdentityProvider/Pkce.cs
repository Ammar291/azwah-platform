using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IdentityProvider;

/// <summary>Proof Key for Code Exchange (RFC 7636), S256 only.</summary>
internal static class Pkce
{
    /// <summary>BASE64URL(SHA-256(...)) is always 43 characters.</summary>
    public static bool IsS256Challenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(IsBase64UrlCharacter);

    /// <summary>A verifier is 43 to 128 unreserved characters whose S256 transform equals the challenge.</summary>
    public static bool Matches(string verifier, string challenge)
    {
        if (verifier.Length is < 43 or > 128 || !verifier.All(IsUnreserved))
        {
            return false;
        }

        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static bool IsBase64UrlCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    private static bool IsUnreserved(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~';
}
