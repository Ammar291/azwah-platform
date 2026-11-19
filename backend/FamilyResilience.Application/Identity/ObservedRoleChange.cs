namespace FamilyResilience.Application.Identity;

/// <summary>
/// What <see cref="ObserveCallerRoles"/> recorded for one request: the roles gained and lost, and whether this was the
/// subject's first observed request (when every role counts as gained). Empty when nothing changed, or when another
/// request recorded the same change first.
/// </summary>
public sealed record ObservedRoleChange(bool FirstSeen, RoleChange Change)
{
    public static ObservedRoleChange None { get; } = new(false, new RoleChange([], []));
}
