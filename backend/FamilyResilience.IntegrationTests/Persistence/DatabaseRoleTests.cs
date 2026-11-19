using FamilyResilience.Infrastructure;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// The shipped role scripts on their own database: the migrator (not a superuser) can run every migration through
/// <c>ConnectionStrings:Migrations</c>, and the API's role can change rows but never the schema or the audit trail.
/// </summary>
public sealed class DatabaseRoleTests : IAsyncLifetime
{
    private const string InsufficientPrivilege = "42501";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    private string Superuser => _postgres.GetConnectionString();

    private string AsApp => DatabaseRoles.As(Superuser, DatabaseRoles.App);

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _postgres.StartAsync(cancellationToken);
        await DatabaseRoles.ExecuteAsync(Superuser, DatabaseRoles.Script("database-roles.sql"), cancellationToken);
        await DatabaseRoles.ExecuteAsync(
            Superuser,
            $"ALTER ROLE {DatabaseRoles.Migrator} PASSWORD '{DatabaseRoles.Password}'; ALTER ROLE {DatabaseRoles.App} PASSWORD '{DatabaseRoles.Password}';",
            cancellationToken);

        // Migrate the way a deployment does: Default is the application role, Migrations the owner.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = AsApp,
            ["ConnectionStrings:Migrations"] = DatabaseRoles.As(Superuser, DatabaseRoles.Migrator),
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).AddLogging();
        services.AddInfrastructure();
        await using var provider = services.BuildServiceProvider();
        await DatabaseMigrator.MigrateAsync(provider, cancellationToken);
        await DatabaseRoles.ExecuteAsync(
            DatabaseRoles.As(Superuser, DatabaseRoles.Migrator), DatabaseRoles.Script("application-role.sql"), cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private async Task<int> RunAsAppAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AsApp);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task AssertRefusedAsync(string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsAppAsync(sql));
        Assert.Equal(InsufficientPrivilege, error.SqlState);
    }

    private const string InsertAudit =
        """
        INSERT INTO audit_events (id, occurred_at, actor_type, actor_id, action, resource_type, resource_id, outcome, correlation_id)
        VALUES (gen_random_uuid(), now(), 'System', NULL, 'system.role.tested', 'integration_test', 'role-test', 'Succeeded', NULL)
        """;

    [Fact]
    public async Task The_migrator_owns_every_table_without_being_a_superuser()
    {
        await using var connection = new NpgsqlConnection(Superuser);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner <> 'azwah_migrator'", connection);

        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using var superuser = new NpgsqlCommand("SELECT rolsuper FROM pg_roles WHERE rolname = 'azwah_migrator'", connection);
        Assert.Equal(false, await superuser.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_application_role_adds_and_reads_audit_events()
    {
        Assert.Equal(1, await RunAsAppAsync(InsertAudit));
        await using var connection = new NpgsqlConnection(AsApp);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit_events WHERE resource_id = 'role-test'", connection);
        Assert.True((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))! >= 1);
    }

    [Theory]
    [InlineData("UPDATE audit_events SET outcome = 'Failed'")]
    [InlineData("DELETE FROM audit_events")]
    [InlineData("TRUNCATE audit_events")]
    public Task The_application_role_cannot_change_the_audit_trail(string sql) => AssertRefusedAsync(sql);

    [Theory]
    [InlineData("CREATE TABLE intruder (id int)")]
    [InlineData("ALTER TABLE households ADD COLUMN intruder text")]
    [InlineData("DROP TABLE outbox_messages")]
    [InlineData("TRUNCATE households")]
    [InlineData("DELETE FROM \"__EFMigrationsHistory\"")]
    public Task The_application_role_cannot_change_the_schema_or_the_migration_history(string sql) => AssertRefusedAsync(sql);

    [Fact]
    public async Task The_application_role_changes_rows_and_takes_row_locks()
    {
        Assert.Equal(0, await RunAsAppAsync("UPDATE households SET updated_at = updated_at WHERE false"));
        Assert.Equal(0, await RunAsAppAsync("DELETE FROM idempotency_keys WHERE false"));
        Assert.Equal(-1, await RunAsAppAsync("SELECT id FROM assessment_sessions FOR UPDATE"));
    }

    [Fact]
    public async Task Later_migrations_tables_get_the_same_data_permissions()
    {
        await DatabaseRoles.ExecuteAsync(
            DatabaseRoles.As(Superuser, DatabaseRoles.Migrator), "CREATE TABLE later_table (id int PRIMARY KEY)", TestContext.Current.CancellationToken);

        Assert.Equal(1, await RunAsAppAsync("INSERT INTO later_table VALUES (1)"));
        await AssertRefusedAsync("ALTER TABLE later_table ADD COLUMN intruder text");
    }
}
