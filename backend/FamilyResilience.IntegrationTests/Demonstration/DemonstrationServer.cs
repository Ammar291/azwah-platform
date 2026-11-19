using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>
/// A PostgreSQL server of its own for <c>db demo</c> against databases that already exist. Each test configures its
/// own ordinary database name, so each gets its own <c>&lt;name&gt;_demo</c> target on the one server; the ordinary
/// database itself is never created, because the command never connects to it.
/// </summary>
public sealed class DemonstrationServer : IAsyncLifetime
{
    /// <summary>An early migration: households exist, with five columns and no guards of their own yet.</summary>
    public const string OlderMigration = "AddAssignmentMemberIndex";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    /// <summary>The configured <c>ConnectionStrings:Default</c> for an ordinary database of this name.</summary>
    public string Ordinary(string database) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

    public DemonstrationTarget Target(string ordinaryDatabase) => DemonstrationDatabase.TargetFor(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Ordinary(ordinaryDatabase) })
        .Build());

    public Task<int?> RunAsync(string ordinaryDatabase, string[] args, CancellationToken cancellationToken) =>
        DemonstrationCommands.RunAsync(Ordinary(ordinaryDatabase), args, cancellationToken);

    /// <summary>Creates the target and applies the platform's migrations up to <paramref name="migration"/> (all when null).</summary>
    public async Task<DemonstrationTarget> MigratedAsync(string ordinaryDatabase, string? migration, CancellationToken cancellationToken)
    {
        var target = Target(ordinaryDatabase);
        Assert.True(await DemonstrationDatabase.CreateIfAbsentAsync(target, cancellationToken));
        await using var db = Context(target);
        var targetMigration = migration is null
            ? null
            : db.Database.GetMigrations().Single(id => id.EndsWith($"_{migration}", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);
        return target;
    }

    public static FamilyResilienceDbContext Context(DemonstrationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var options = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
        FamilyResilienceDbContextOptions.Configure(options, target.ApplicationConnectionString);
        return new FamilyResilienceDbContext(options.Options);
    }

    public static async Task<object?> ScalarAsync(
        DemonstrationTarget target, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(parameters);
        await using var connection = new NpgsqlConnection(target.ApplicationConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>A household of the owner's own, written with the columns every migration since the core model has.</summary>
    public static Task InsertForeignHouseholdAsync(DemonstrationTarget target, string reference, CancellationToken cancellationToken) =>
        ScalarAsync(
            target,
            "INSERT INTO households (id, reference, status, created_at, updated_at) VALUES (gen_random_uuid(), @reference, 'Active', now(), now())",
            cancellationToken,
            ("reference", reference));

    public static async Task<bool> HasHouseholdAsync(DemonstrationTarget target, string reference, CancellationToken cancellationToken) =>
        await ScalarAsync(target, "SELECT count(*) FROM households WHERE reference = @reference", cancellationToken, ("reference", reference))
            is long count && count == 1;

    /// <summary>The marker the seeder writes when a build completes.</summary>
    public static Task InsertMarkerAsync(DemonstrationTarget target, CancellationToken cancellationToken) =>
        ScalarAsync(
            target,
            "INSERT INTO audit_events (id, occurred_at, actor_type, actor_id, action, resource_type, outcome) "
            + "VALUES (gen_random_uuid(), now(), 'User', @actor, @action, 'demonstration_database', 'Succeeded')",
            cancellationToken,
            ("actor", DemonstrationDataSeeder.SeedSubject),
            ("action", DemonstrationDataSeeder.SeededAuditAction));

    public static async Task<bool> IsBuiltAsync(DemonstrationTarget target, CancellationToken cancellationToken)
    {
        await using var db = Context(target);
        return !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
            && await db.Waves.AnyAsync(wave => wave.Code == DemonstrationDataSeeder.WaveOneCode, cancellationToken)
            && await db.Households.CountAsync(cancellationToken) == 60
            && await db.AuditEvents.AnyAsync(audit => audit.Action == DemonstrationDataSeeder.SeededAuditAction, cancellationToken);
    }

    /// <summary>Everything a command could change: applied migrations, tables in every schema and the rows in each.</summary>
    public static async Task<string> SnapshotAsync(DemonstrationTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var lines = new List<string>();
        await using var connection = new NpgsqlConnection(target.ApplicationConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var migrations = new NpgsqlCommand("SELECT migration_id FROM \"__EFMigrationsHistory\" ORDER BY 1", connection))
        await using (var reader = await migrations.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add($"migration {reader.GetString(0)}");
            }
        }

        await using (var tables = new NpgsqlCommand(
            "SELECT table_schema || '.' || table_name FROM information_schema.tables "
            + "WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY 1",
            connection))
        await using (var reader = await tables.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add($"table {reader.GetString(0)}");
            }
        }

        foreach (var (table, rows) in await DemonstrationApi.RowCountsAsync(target.ApplicationConnectionString, cancellationToken))
        {
            lines.Add($"rows {table} {rows}");
        }

        return string.Join('\n', lines);
    }

    public async ValueTask InitializeAsync() => await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}
