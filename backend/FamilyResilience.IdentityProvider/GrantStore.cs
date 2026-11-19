using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Authorization codes and refresh tokens, in memory only (they die with the process). Stores hashes, not the values.
/// Codes are single-use and live 60 seconds; a second use revokes what the first issued. Refresh tokens rotate on every
/// use within a family that ends 8 hours after sign-in; presenting a rotated token again revokes the whole family.
/// </summary>
internal sealed class GrantStore(TimeProvider time)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RefreshFamilyLifetime = TimeSpan.FromHours(8);

    // Redeemed codes are remembered a while after they expire, so a late second use still counts as reuse.
    private static readonly TimeSpan RedeemedCodeMemory = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefreshEntry> _refreshTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Family> _families = [];

    public string IssueCode(AuthorizationGrant grant)
    {
        var code = NewSecret();
        lock (_gate)
        {
            var now = time.GetUtcNow();
            Purge(now);
            _codes[Key(code)] = new CodeEntry(grant, now + CodeLifetime);
        }

        return code;
    }

    /// <summary>Uses up the code, whatever the outcome: a code is never accepted twice.</summary>
    public CodeRedemption RedeemCode(string code)
    {
        var key = Key(code);
        lock (_gate)
        {
            if (!_codes.TryGetValue(key, out var entry))
            {
                return new CodeRedemption(GrantOutcome.Invalid, null, null);
            }

            if (entry.Redeemed)
            {
                entry.Compromised = true;
                if (entry.FamilyId is { } familyId && _families.TryGetValue(familyId, out var family))
                {
                    family.Revoked = true;
                }

                return new CodeRedemption(GrantOutcome.Reused, null, null);
            }

            entry.Redeemed = true;
            return time.GetUtcNow() >= entry.ExpiresAt
                ? new CodeRedemption(GrantOutcome.Invalid, null, null)
                : new CodeRedemption(GrantOutcome.Accepted, entry.Grant, key);
        }
    }

    /// <summary>
    /// Records that tokens are being issued for a redeemed code, starting its refresh token family when the grant allows
    /// one. False when the code was presented again in the meantime: then nothing may be issued.
    /// </summary>
    public bool TryIssueForCode(string codeKey, out string? refreshToken)
    {
        refreshToken = null;
        lock (_gate)
        {
            if (!_codes.TryGetValue(codeKey, out var entry) || entry.Compromised)
            {
                return false;
            }

            if (entry.Grant.AllowsRefresh)
            {
                var family = new Family(entry.Grant, time.GetUtcNow() + RefreshFamilyLifetime);
                _families[family.Id] = family;
                entry.FamilyId = family.Id;
                refreshToken = AddRefreshToken(family);
            }

            return true;
        }
    }

    /// <param name="requestedScope">A narrower scope for the new access token, or null to keep the granted scope.</param>
    public RefreshRotation Rotate(string refreshToken, string clientId, IReadOnlyCollection<string>? requestedScope)
    {
        lock (_gate)
        {
            if (!_refreshTokens.TryGetValue(Key(refreshToken), out var entry)
                || !_families.TryGetValue(entry.FamilyId, out var family)
                || family.Revoked
                || !string.Equals(family.Grant.ClientId, clientId, StringComparison.Ordinal))
            {
                return new RefreshRotation(GrantOutcome.Invalid, null, null, null);
            }

            if (entry.Rotated)
            {
                family.Revoked = true;
                return new RefreshRotation(GrantOutcome.Reused, null, null, null);
            }

            if (time.GetUtcNow() >= family.ExpiresAt)
            {
                return new RefreshRotation(GrantOutcome.Invalid, null, null, null);
            }

            var granted = family.Grant.Scope.Split(' ');
            if (requestedScope is not null && !requestedScope.All(scope => granted.Contains(scope, StringComparer.Ordinal)))
            {
                return new RefreshRotation(GrantOutcome.ScopeExceeded, null, null, null);
            }

            entry.Rotated = true;
            var scope = requestedScope is null
                ? family.Grant.Scope
                : string.Join(' ', granted.Where(value => requestedScope.Contains(value, StringComparer.Ordinal)));
            return new RefreshRotation(GrantOutcome.Accepted, family.Grant, scope, AddRefreshToken(family));
        }
    }

    /// <summary>Revokes the refresh token's family when it was issued to <paramref name="clientId"/>; otherwise does nothing.</summary>
    public void Revoke(string token, string clientId)
    {
        lock (_gate)
        {
            if (_refreshTokens.TryGetValue(Key(token), out var entry)
                && _families.TryGetValue(entry.FamilyId, out var family)
                && string.Equals(family.Grant.ClientId, clientId, StringComparison.Ordinal))
            {
                family.Revoked = true;
            }
        }
    }

    private static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static string Key(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private string AddRefreshToken(Family family)
    {
        var token = NewSecret();
        var key = Key(token);
        _refreshTokens[key] = new RefreshEntry(family.Id);
        family.TokenKeys.Add(key);
        return token;
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var (key, _) in _codes.Where(code => now >= code.Value.ExpiresAt + RedeemedCodeMemory).ToList())
        {
            _codes.Remove(key);
        }

        foreach (var (id, family) in _families.Where(family => now >= family.Value.ExpiresAt).ToList())
        {
            family.TokenKeys.ForEach(key => _refreshTokens.Remove(key));
            _families.Remove(id);
        }
    }

    private sealed class CodeEntry(AuthorizationGrant grant, DateTimeOffset expiresAt)
    {
        public AuthorizationGrant Grant { get; } = grant;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public bool Redeemed { get; set; }

        public bool Compromised { get; set; }

        public Guid? FamilyId { get; set; }
    }

    private sealed class RefreshEntry(Guid familyId)
    {
        public Guid FamilyId { get; } = familyId;

        public bool Rotated { get; set; }
    }

    private sealed class Family(AuthorizationGrant grant, DateTimeOffset expiresAt)
    {
        public Guid Id { get; } = Guid.NewGuid();

        public AuthorizationGrant Grant { get; } = grant;

        /// <summary>Absolute: rotation never extends it.</summary>
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public bool Revoked { get; set; }

        public List<string> TokenKeys { get; } = [];
    }
}
