using System.Diagnostics;
using System.Globalization;
using FamilyResilience.Api.Identity;
using FamilyResilience.Application;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Infrastructure;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.Api.Database;

/// <summary>
/// <c>db demo [--reset [--replace-foreign-database]]</c>, Development only (checked by <see cref="DatabaseCommands"/>):
/// builds the local demonstration database (D-140) beside the configured one. The target is always
/// <c>&lt;configured name&gt;_demo</c> (<see cref="DemonstrationDatabase"/>; the configured database itself when its
/// name already ends in <c>_demo</c>). Before anything drops or migrates it, the target is inspected: an absent or empty
/// database, or one carrying the marker of a completed build, is used; any other is refused and left untouched, unless
/// <c>--reset --replace-foreign-database</c> asks for it to be dropped. It is then created when absent, migrated as the
/// schema owner and seeded through services of its own that never see the configured database. <c>--reset</c> drops
/// it first, so a demonstration used live can be wiped and rebuilt. Prints counts and the database's name, never a
/// connection string.
/// </summary>
internal static class DemonstrationCommand
{
    public const string ResetOption = "--reset";

    /// <summary>The explicit opt-in to drop a database that <c>db demo</c> did not make. Never the default, never in an npm script.</summary>
    public const string ReplaceForeignDatabaseOption = "--replace-foreign-database";

    public const string Usage = "Usage: db demo [--reset [--replace-foreign-database]]";

    public static string ForeignDatabaseRefusal(string database) =>
        $"The database '{database}' was not made by 'db demo': it holds data and no demonstration marker. It was left untouched "
        + "(nothing was dropped, migrated or written). To delete everything in it and build the demonstration in its place, run "
        + $"'db demo {ResetOption} {ReplaceForeignDatabaseOption}'.";

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = args.Skip(2).ToList();
        var reset = options.Contains(ResetOption);
        var replaceForeign = options.Contains(ReplaceForeignDatabaseOption);
        if (options.Any(option => option is not (ResetOption or ReplaceForeignDatabaseOption)) || (replaceForeign && !reset))
        {
            throw new InvalidOperationException(Usage);
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var target = DemonstrationDatabase.TargetFor(configuration);
        var elapsed = Stopwatch.StartNew();

        // Read before anything changes it: a database the command did not make is never migrated, seeded or dropped
        // on its own say-so, whatever its name.
        var state = await DemonstrationDatabase.InspectAsync(target, cancellationToken);
        if (state == DemonstrationDatabaseState.Foreign && !replaceForeign)
        {
            throw new InvalidOperationException(ForeignDatabaseRefusal(target.DatabaseName));
        }

        if (reset && state != DemonstrationDatabaseState.Absent)
        {
            await DemonstrationDatabase.DropAsync(target, cancellationToken);
            Console.WriteLine(state == DemonstrationDatabaseState.Foreign
                ? $"Dropped the database '{target.DatabaseName}', which 'db demo' had not made ({ReplaceForeignDatabaseOption})."
                : $"Dropped the demonstration database '{target.DatabaseName}'.");
        }

        if (await DemonstrationDatabase.CreateIfAbsentAsync(target, cancellationToken))
        {
            Console.WriteLine($"Created the demonstration database '{target.DatabaseName}'.");
        }

        await using var demonstration = Services(configuration, target, services.GetRequiredService<TimeProvider>());
        await DatabaseMigrator.MigrateAsync(demonstration, cancellationToken);
        var built = await DemonstrationDataSeeder.SeedAsync(demonstration, cancellationToken);
        foreach (var line in await DemonstrationDataSeeder.SummaryAsync(demonstration, cancellationToken))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(built
            ? string.Create(CultureInfo.InvariantCulture, $"Built the demonstration in {elapsed.Elapsed.TotalSeconds:0.0} s.")
            : "The demonstration database was already built; nothing changed. 'db demo --reset' wipes and rebuilds it.");
        return 0;
    }

    /// <summary>The platform's services against the demonstration database only.</summary>
    private static ServiceProvider Services(IConfiguration configuration, DemonstrationTarget target, TimeProvider timeProvider)
    {
        var overrides = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{FamilyResilienceDbContextOptions.ConnectionStringName}"] = target.ApplicationConnectionString,
        };
        if (target.OwnerConnectionString is { } owner)
        {
            overrides[$"ConnectionStrings:{DatabaseMigrator.MigrationsConnectionStringName}"] = owner;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(overrides).Build());
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddApplication();
        services.AddInfrastructure();
        // No request: the platform itself is acting. The seed records its own subject on what it approves and audits.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
