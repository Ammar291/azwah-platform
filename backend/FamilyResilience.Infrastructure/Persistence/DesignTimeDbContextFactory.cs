using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` to create migrations. Creating a migration never opens a connection, so this
/// placeholder holds no credentials.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FamilyResilienceDbContext>
{
    public FamilyResilienceDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
        FamilyResilienceDbContextOptions.Configure(builder, "Host=localhost;Database=design_time_only");
        return new FamilyResilienceDbContext(builder.Options);
    }
}
