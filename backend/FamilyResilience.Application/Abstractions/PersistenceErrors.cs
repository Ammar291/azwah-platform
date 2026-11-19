using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Abstractions;

public static class PersistenceErrors
{
    public static readonly Error ConcurrencyConflict =
        new("persistence.concurrency_conflict", "Someone else changed this record. Reload it and try again.");

    public static readonly Error Conflict =
        new("persistence.conflict", "The change conflicts with an existing record.");
}
