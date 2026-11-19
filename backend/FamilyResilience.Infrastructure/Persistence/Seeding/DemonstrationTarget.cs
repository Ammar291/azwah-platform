namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// The demonstration database derived from the configured connection strings: its name (always ending in
/// <c>_demo</c>), the application's connection to it and, when <c>ConnectionStrings:Migrations</c> is set, the schema
/// owner's. The connection strings hold credentials: never log or print them.
/// </summary>
public sealed record DemonstrationTarget(string DatabaseName, string ApplicationConnectionString, string? OwnerConnectionString)
{
    /// <summary>The connection that creates, drops and migrates the database: the owner's when set (D-86).</summary>
    public string AdministrationConnectionString => OwnerConnectionString ?? ApplicationConnectionString;
}
