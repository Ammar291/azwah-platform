using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>Where the provider sent the browser back to, and the parameters it added.</summary>
public sealed record AuthorizationResponse(string Target, Dictionary<string, StringValues> Parameters)
{
    public static AuthorizationResponse From(Uri location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var text = location.OriginalString;
        var query = text.IndexOf('?', StringComparison.Ordinal);
        return query < 0
            ? new AuthorizationResponse(text, [])
            : new AuthorizationResponse(text[..query], QueryHelpers.ParseQuery(text[query..]));
    }

    public string? this[string key] => Parameters.TryGetValue(key, out var value) ? value.ToString() : null;

    public string Require(string key) => this[key] ?? throw new InvalidOperationException($"The redirect has no '{key}'.");
}
