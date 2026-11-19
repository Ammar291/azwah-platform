using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.Application.Abstractions;

/// <summary>A stable fingerprint of a request, so a reused idempotency key with a different request is recognised.</summary>
public static class RequestHash
{
    /// <summary>SHA-256 over the parts joined by a separator that cannot appear in them, in lowercase hexadecimal.</summary>
    public static string Of(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts))));
    }
}
