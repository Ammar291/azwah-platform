using FamilyResilience.Domain.Audit;
using FamilyResilience.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class PersistenceModelTests
{
    private static FamilyResilienceDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
        // Building the model never opens a connection.
        FamilyResilienceDbContextOptions.Configure(builder, "Host=localhost;Database=model_only");
        return new FamilyResilienceDbContext(builder.Options);
    }

    private static IEntityType AuditEventType(FamilyResilienceDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AuditEvent))!;

    [Fact]
    public void Audit_events_use_a_snake_case_table_and_columns()
    {
        using var context = CreateContext();
        var entity = AuditEventType(context);

        Assert.Equal("audit_events", entity.GetTableName());
        Assert.Equal(
            new[] { "action", "actor_id", "actor_type", "correlation_id", "id", "occurred_at", "outcome", "resource_id", "resource_type" },
            entity.GetProperties().Select(p => p.GetColumnName()).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Audit_enums_are_stored_as_text_and_time_as_timestamptz()
    {
        using var context = CreateContext();
        var entity = AuditEventType(context);

        Assert.Equal("character varying(20)", entity.FindProperty(nameof(AuditEvent.Outcome))!.GetColumnType());
        Assert.Equal("character varying(20)", entity.FindProperty(nameof(AuditEvent.ActorType))!.GetColumnType());
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(AuditEvent.OccurredAt))!.GetColumnType());
        Assert.Equal(AuditEvent.MaxActionLength, entity.FindProperty(nameof(AuditEvent.Action))!.GetMaxLength());
    }

    [Fact]
    public void Model_has_no_changes_missing_from_the_migrations()
    {
        using var context = CreateContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> (see backend/README.md).");
    }
}
