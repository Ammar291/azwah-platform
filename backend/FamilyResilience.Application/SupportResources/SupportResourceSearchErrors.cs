using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.SupportResources;

public static class SupportResourceSearchErrors
{
    /// <summary>
    /// A server fault (the database) stopped a participant's search after a crisis signal (500). Only then is the fault
    /// caught rather than left to the global handler, so the problem can still carry the crisis notice (D-138).
    /// </summary>
    public static readonly Error Faulted =
        new("support_resources.search_faulted", "The search could not be completed. Try again.");
}
