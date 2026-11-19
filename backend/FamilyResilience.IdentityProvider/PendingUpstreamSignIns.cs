using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Authorization requests waiting for an upstream sign-in (UAE PASS or Google) to send the browser back, keyed by the
/// hash of a random upstream <c>state</c>. Each entry remembers which upstream it was sent to, so a state issued for one
/// is refused at the other's callback, and the PKCE verifier when that upstream uses one. In memory only, single use,
/// and 10 minutes at most; a full store refuses new sign-ins rather than grow.
/// </summary>
internal sealed class PendingUpstreamSignIns(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int Capacity = 1000;

    public const string UaePass = "uae_pass";
    public const string Google = "google";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (PendingUpstreamSignIn SignIn, DateTimeOffset ExpiresAt)> _pending = new(StringComparer.Ordinal);

    /// <summary>The upstream state to send, or null when too many sign-ins are already waiting.</summary>
    /// <param name="upstream"><see cref="UaePass"/> or <see cref="Google"/>.</param>
    /// <param name="codeVerifier">The PKCE verifier sent with the upstream code exchange, when the upstream uses PKCE.</param>
    public string? Add(AuthorizationRequest request, string upstream, string? codeVerifier = null)
    {
        var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        lock (_gate)
        {
            var now = time.GetUtcNow();
            foreach (var key in _pending.Where(entry => now >= entry.Value.ExpiresAt).Select(entry => entry.Key).ToList())
            {
                _pending.Remove(key);
            }

            if (_pending.Count >= Capacity)
            {
                return null;
            }

            _pending[Key(state)] = (new PendingUpstreamSignIn(request, upstream, codeVerifier), now + Lifetime);
        }

        return state;
    }

    /// <summary>
    /// Uses up the state: the waiting sign-in, or null when it is unknown, expired, already used or was sent to another
    /// upstream (it is used up either way).
    /// </summary>
    public PendingUpstreamSignIn? Take(string state, string upstream)
    {
        lock (_gate)
        {
            if (!_pending.Remove(Key(state), out var entry))
            {
                return null;
            }

            return time.GetUtcNow() < entry.ExpiresAt && string.Equals(entry.SignIn.Upstream, upstream, StringComparison.Ordinal)
                ? entry.SignIn
                : null;
        }
    }

    private static string Key(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
