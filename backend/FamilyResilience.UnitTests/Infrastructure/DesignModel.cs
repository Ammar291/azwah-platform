using FamilyResilience.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The design-time EF model. Building it never opens a connection.</summary>
internal static class DesignModel
{
    public static IModel Load()
    {
        var builder = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
        FamilyResilienceDbContextOptions.Configure(builder, "Host=localhost;Database=model_only");
        using var context = new FamilyResilienceDbContext(builder.Options);
        return context.GetService<IDesignTimeModel>().Model;
    }
}
