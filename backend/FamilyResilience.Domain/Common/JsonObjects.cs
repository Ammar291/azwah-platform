using System.Text.Json;

namespace FamilyResilience.Domain.Common;

/// <summary>Strict reading of JSON rule definitions: only known keys, each at most once.</summary>
internal static class JsonObjects
{
    /// <summary>The first key that is not allowed or appears twice; null when every key is allowed and appears once.</summary>
    public static string? FindUnexpectedKey(JsonElement element, IReadOnlyCollection<string> allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
            {
                return property.Name;
            }
        }

        return null;
    }
}
