using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.Api.Database;

/// <summary>
/// <c>Database:UseDemonstrationDatabase=true</c> (Development only, D-140) points the API at the demonstration database
/// that <c>db demo</c> builds: the configured connection strings keep their server and credentials and get the database
/// <c>&lt;name&gt;_demo</c>. Applied before the host is built, so every service sees the same database. Any other
/// environment refuses to start with it here, and the deployed-settings guard names it too
/// (<see cref="Hosting.SecurityConfigurationGuard"/>).
/// </summary>
public static class DemonstrationDatabaseSetting
{
    public static void Apply(IConfigurationManager configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        if (!DemonstrationDatabase.IsRequested(configuration))
        {
            return;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{DemonstrationDatabase.UseDemonstrationDatabaseSetting} is allowed only in the Development environment.");
        }

        var target = DemonstrationDatabase.TargetFor(configuration);
        var overrides = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{FamilyResilienceDbContextOptions.ConnectionStringName}"] = target.ApplicationConnectionString,
        };
        if (target.OwnerConnectionString is { } owner)
        {
            overrides[$"ConnectionStrings:{DatabaseMigrator.MigrationsConnectionStringName}"] = owner;
        }

        configuration.AddInMemoryCollection(overrides);
        Console.WriteLine(
            $"Using the demonstration database '{target.DatabaseName}' (LOCAL DEMONSTRATION ONLY, D-140; built with 'db demo').");
    }
}
