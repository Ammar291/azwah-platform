namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>What <see cref="DemonstrationDatabase.InspectAsync"/> found in the demonstration database before anything changes it.</summary>
public enum DemonstrationDatabaseState
{
    /// <summary>No database of that name exists.</summary>
    Absent = 1,

    /// <summary>It exists and no table in it holds a row (EF Core's migration history aside): created, or migrated, and never seeded.</summary>
    Empty = 2,

    /// <summary>It carries the marker a completed <c>db demo</c> build writes, whatever migration it is on.</summary>
    Demonstration = 3,

    /// <summary>It holds rows and no marker: <c>db demo</c> did not make it, so nothing may drop, migrate or seed it.</summary>
    Foreign = 4,
}
