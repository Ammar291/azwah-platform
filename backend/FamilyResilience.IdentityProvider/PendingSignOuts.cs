using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Sign-outs waiting for UAE PASS to send the browser back (D-129): the client's validated post-logout redirect URI
/// and <c>state</c>, keyed by the hash of a random single-use key. In memory only, 10 minutes at most; a full store
/// refuses new sign-outs rather than grow. Same shape as <see cref="PendingUpstreamSignIns"/>.
/// </summary>
internal sealed class PendingSignOuts(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int Capacity = 1000;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Uri, string? State, DateTimeOffset ExpiresAt)> _pending = new(StringComparer.Ordinal);

    /// <summary>The key to send through UAE PASS, or null when too many sign-outs are already waiting.</summary>
    public string? Add(string postLogoutRedirectUri, string? state)
    {
        var key = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _pending.Where(entry => now >= entry.Value.ExpiresAt).Select(entry => entry.Key).ToList())
            {
                _pending.Remove(expired);
            }

            if (_pending.Count >= Capacity)
            {
                return null;
            }

            _pending[Hash(key)] = (postLogoutRedirectUri, state, now + Lifetime);
        }

        return key;
    }

    /// <summary>Uses up the key: where to send the browser, or null when it is unknown, expired or already used.</summary>
    public (string Uri, string? State)? Take(string key)
    {
        lock (_gate)
        {
            if (!_pending.Remove(Hash(key), out var entry))
            {
                return null;
            }

            return time.GetUtcNow() < entry.ExpiresAt ? (entry.Uri, entry.State) : null;
        }
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
