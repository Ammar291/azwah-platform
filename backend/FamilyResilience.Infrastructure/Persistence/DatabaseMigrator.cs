using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// The schema owner's connection (<c>ConnectionStrings:Migrations</c>). When it is set, migrations run with it, so the
    /// API's own role (<c>ConnectionStrings:Default</c>) never needs to change the schema (D-86).
    /// </summary>
    public const string MigrationsConnectionStringName = "Migrations";

    /// <summary>Applies pending migrations. Development and tests only; production runs a migration bundle.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var owner = services.GetRequiredService<IConfiguration>().GetConnectionString(MigrationsConnectionStringName);
        if (!string.IsNullOrWhiteSpace(owner))
        {
            var options = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
            FamilyResilienceDbContextOptions.Configure(options, owner);
            await using var ownerContext = new FamilyResilienceDbContext(options.Options);
            await ownerContext.Database.MigrateAsync(cancellationToken);
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public static async Task<bool> HasPendingMigrationsAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
    }
}
