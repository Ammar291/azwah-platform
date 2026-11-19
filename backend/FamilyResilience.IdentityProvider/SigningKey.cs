using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The RS256 signing key (RFC 7515/7519), published as a JWK. Configured (<see cref="SigningKeyOptions.Pem"/>) it is
/// stable across restarts and its key ID is derived from the public key; without a PEM (Development only, D-129) an
/// RSA-2048 key is made when the process starts and every token dies with it.
/// </summary>
internal sealed class SigningKey : IDisposable
{
    public const string Algorithm = "RS256";
    public const int MinimumKeyBits = 2048;

    private readonly RSA _rsa;

    public SigningKey(IOptions<IdentityProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Value.SigningKey.Pem is { } pem && !string.IsNullOrWhiteSpace(pem))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(pem);
            KeyId = Base64Url.EncodeToString(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo()))[..16];
        }
        else
        {
            _rsa = RSA.Create(MinimumKeyBits);
            KeyId = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        }
    }

    public string KeyId { get; }

    /// <summary>
    /// Why a configured PEM cannot be used: <c>unparseable</c> (not an unencrypted RSA private key) or <c>too_short</c>
    /// (under <see cref="MinimumKeyBits"/>); null when it is fine. The PEM itself is never part of the answer.
    /// </summary>
    public static string? Problem(string pem)
    {
        ArgumentNullException.ThrowIfNull(pem);
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            // A public key alone imports too; only a private key can sign.
            rsa.ExportParameters(includePrivateParameters: true);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            return "unparseable";
        }

        return rsa.KeySize < MinimumKeyBits ? "too_short" : null;
    }

    public string CreateJwt(string type, JsonObject claims)
    {
        var header = new JsonObject { ["alg"] = Algorithm, ["typ"] = type, ["kid"] = KeyId };
        var signingInput = $"{Encode(header)}.{Encode(claims)}";
        var signature = _rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>The claims of a JWT this key signed, or null for anything else (expiry is not checked).</summary>
    public JsonObject? ReadOwnJwt(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var header = JsonNode.Parse(Base64Url.DecodeFromChars(parts[0])) as JsonObject;
            if (header?["alg"]?.GetValue<string>() != Algorithm || header["kid"]?.GetValue<string>() != KeyId)
            {
                return null;
            }

            var valid = _rsa.VerifyData(
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                Base64Url.DecodeFromChars(parts[2]),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return valid ? JsonNode.Parse(Base64Url.DecodeFromChars(parts[1])) as JsonObject : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public JsonObject PublicJwk()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return new JsonObject
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = Algorithm,
            ["kid"] = KeyId,
            ["n"] = Base64Url.EncodeToString(parameters.Modulus),
            ["e"] = Base64Url.EncodeToString(parameters.Exponent),
        };
    }

    public void Dispose() => _rsa.Dispose();

    private static string Encode(JsonObject json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json.ToJsonString()));
}
