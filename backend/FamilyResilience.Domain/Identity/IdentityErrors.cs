using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Identity;

public static class IdentityErrors
{
    public static readonly Error SubjectInvalid =
        new("identity.subject_invalid", "The token subject is empty or longer than 200 characters.");

    public static readonly Error RolesInvalid =
        new("identity.roles_invalid", "A role snapshot holds at most 20 snake-case role codes of up to 64 characters.");
}
