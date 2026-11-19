namespace FamilyResilience.Application.Identity;

/// <summary>
/// The platform roles a subject gained and lost between two observations of their token. Only codes in
/// <see cref="Roles.All"/> count: any other string in a token grants nothing, so it is not recorded either.
/// </summary>
public sealed record RoleChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>The known platform roles among <paramref name="roles"/>, once each, in ordinal order.</summary>
    public static IReadOnlyList<string> Known(IEnumerable<string> roles) =>
        roles.Where(role => Roles.All.Contains(role, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    public static RoleChange Between(IEnumerable<string> before, IEnumerable<string> after)
    {
        var previous = Known(before);
        var current = Known(after);
        return new RoleChange(
            current.Except(previous, StringComparer.Ordinal).ToList(),
            previous.Except(current, StringComparer.Ordinal).ToList());
    }
}
