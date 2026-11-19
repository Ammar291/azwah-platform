using FamilyResilience.Application.Households;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.Api.Database;

/// <summary>
/// One-off database commands that run instead of the web host: <c>dotnet run -- db migrate</c>,
/// <c>dotnet run -- db seed</c>, <c>db demo [--reset [--replace-foreign-database]]</c> and
/// <c>db erase-household --household &lt;id&gt; --reference &lt;reference&gt; [--withdraw]</c>.
/// Seeding synthetic data and building the demonstration database (<see cref="DemonstrationCommand"/>, D-140) are
/// refused outside the Development environment. Erasure runs in any environment, so an approved request can be
/// honoured; it prints the rows deleted per table, never data (D-88).
/// </summary>
public static class DatabaseCommands
{
    public static async Task<int?> TryRunAsync(
        string[] args, IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        if (args.Length < 2 || args[0] != "db")
        {
            return null;
        }

        switch (args[1])
        {
            case "migrate":
                await DatabaseMigrator.MigrateAsync(services, cancellationToken);
                return 0;

            case "seed":
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "Synthetic development data can only be seeded in the Development environment.");
                }

                if (await DatabaseMigrator.HasPendingMigrationsAsync(services, cancellationToken))
                {
                    throw new InvalidOperationException("The database has pending migrations. Run 'db migrate' first.");
                }

                await DevelopmentDataSeeder.SeedAsync(services, cancellationToken);
                return 0;

            case "demo":
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException("The demonstration database can only be built in the Development environment.");
                }

                return await DemonstrationCommand.RunAsync(args, services, cancellationToken);

            case "erase-household":
                return await EraseHouseholdAsync(args, services, cancellationToken);

            default:
                throw new InvalidOperationException(
                    "Unknown database command. Use 'db migrate', 'db seed', 'db demo [--reset [--replace-foreign-database]]' or 'db erase-household'.");
        }
    }

    private static async Task<int> EraseHouseholdAsync(string[] args, IServiceProvider services, CancellationToken cancellationToken)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        if (!Guid.TryParse(Option("--household"), out var householdId) || string.IsNullOrWhiteSpace(Option("--reference")))
        {
            throw new InvalidOperationException(
                "Usage: db erase-household --household <household id> --reference <household reference> [--withdraw]");
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<EraseHousehold>()
            .HandleAsync(householdId, Option("--reference"), args.Contains("--withdraw"), cancellationToken);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"The household was not erased: {result.Error!.Code}");
            return 1;
        }

        foreach (var (table, rows) in result.Value.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{table}: {rows}");
        }

        return 0;
    }
}
