using Microsoft.Extensions.Caching.Memory;

namespace FamilyResilience.Api.Identity;

/// <summary>
/// Subjects and role sets this API instance has already observed in the last ten minutes, so a request with the same
/// roles costs no database read. Bounded to 10 000 entries; an entry that falls out only means one more read.
/// </summary>
public sealed class RoleObservationCache : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private MemoryCache _cache = New();

    public bool Contains(string subject, IEnumerable<string> roles) => _cache.TryGetValue(Key(subject, roles), out _);

    public void Remember(string subject, IEnumerable<string> roles) =>
        _cache.Set(Key(subject, roles), true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime, Size = 1 });

    /// <summary>Forgets everything, as a restart would.</summary>
    public void Clear()
    {
        var previous = Interlocked.Exchange(ref _cache, New());
        previous.Dispose();
    }

    public void Dispose() => _cache.Dispose();

    private static string Key(string subject, IEnumerable<string> roles) =>
        $"{subject}\n{string.Join(',', roles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}";

    private static MemoryCache New() => new(new MemoryCacheOptions { SizeLimit = 10_000 });
}
