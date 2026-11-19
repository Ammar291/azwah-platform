using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>Reads a compact JWT and checks its RS256 signature against a JWK, independently of the provider's code.</summary>
public static class TestJwt
{
    public static JsonElement Header(string jwt) => Part(jwt, 0);

    public static JsonElement Payload(string jwt) => Part(jwt, 1);

    public static bool HasValidRs256Signature(string jwt, JsonElement jwk)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        var parts = jwt.Split('.');
        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = FromBase64Url(jwk.GetProperty("n").GetString()!),
            Exponent = FromBase64Url(jwk.GetProperty("e").GetString()!),
        });
        return rsa.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            FromBase64Url(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    public static List<string> Names(JsonElement json) => json.EnumerateObject().Select(property => property.Name).ToList();

    private static JsonElement Part(string jwt, int index)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        using var document = JsonDocument.Parse(FromBase64Url(jwt.Split('.')[index]));
        return document.RootElement.Clone();
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}
