using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Which directory entries a caller may see, decided only from the roles in the validated token.</summary>
public static class SupportResourceAccess
{
    public static IReadOnlyCollection<SupportResourceVisibility> VisibilitiesFor(IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Contains(Roles.SupportCoordinator) || roles.Contains(Roles.SystemAdministrator)
            ? [SupportResourceVisibility.Everyone, SupportResourceVisibility.StaffOnly]
            : [SupportResourceVisibility.Everyone];
    }
}
