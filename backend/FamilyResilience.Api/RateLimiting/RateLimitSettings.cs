using System.Globalization;

namespace FamilyResilience.Api.RateLimiting;

/// <summary>
/// Rate limits from the <c>RateLimiting</c> section. On unless <c>RateLimiting:Enabled</c> is false; the global limit is
/// 300 requests a minute per signed-in subject and 60 per anonymous address (<c>RateLimiting:Global:SignedInPermitLimit</c>,
/// <c>:AnonymousPermitLimit</c>, <c>:WindowSeconds</c>). Every number is kept between 1 and its maximum, so a typo cannot
/// switch a limit off.
/// </summary>
public sealed record RateLimitSettings(
    bool Enabled,
    RateLimitWindow SignedIn,
    RateLimitWindow Anonymous,
    IReadOnlyDictionary<string, RateLimitWindow> Policies)
{
    public const string Section = "RateLimiting";

    private const int MaxPermitLimit = 100_000;
    private const int MaxWindowSeconds = 24 * 60 * 60;

    public static RateLimitSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(Section);
        var global = section.GetSection("Global");
        var globalWindow = Number(global["WindowSeconds"], 60, MaxWindowSeconds);
        var policies = RateLimitPolicies.Defaults.ToDictionary(
            entry => entry.Key,
            entry =>
            {
                var policy = section.GetSection($"Policies:{entry.Key}");
                return new RateLimitWindow(
                    Number(policy["PermitLimit"], entry.Value.PermitLimit, MaxPermitLimit),
                    Number(policy["WindowSeconds"], entry.Value.WindowSeconds, MaxWindowSeconds));
            },
            StringComparer.Ordinal);

        return new RateLimitSettings(
            !string.Equals(section["Enabled"], "false", StringComparison.OrdinalIgnoreCase),
            new RateLimitWindow(Number(global["SignedInPermitLimit"], 300, MaxPermitLimit), globalWindow),
            new RateLimitWindow(Number(global["AnonymousPermitLimit"], 60, MaxPermitLimit), globalWindow),
            policies);
    }

    private static int Number(string? configured, int fallback, int maximum) =>
        int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 1, maximum)
            : fallback;
}
