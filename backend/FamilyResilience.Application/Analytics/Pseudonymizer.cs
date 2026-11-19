using System.Security.Cryptography;
using System.Text;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Keyed pseudonyms for one export: HMAC-SHA-256 under a random key that exists only while the file is written and is
/// then wiped. Rows of one file can be linked to each other; nothing links them to another export or back to the
/// platform's IDs. Members and households are kept apart, so equal ID values never share a pseudonym.
/// </summary>
public sealed class Pseudonymizer : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private bool _disposed;

    public string Member(FamilyMemberId id) => Of("member", id.Value);

    public string Household(HouseholdId id) => Of("household", id.Value);

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private string Of(string kind, Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{kind}:{id:N}"));
        return Convert.ToHexStringLower(mac.AsSpan(0, 16));
    }
}
