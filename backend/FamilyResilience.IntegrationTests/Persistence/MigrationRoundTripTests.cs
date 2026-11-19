using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class MigrationRoundTripTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Migrations_revert_to_initial_create_and_reapply_on_an_empty_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var database = $"roundtrip_{Guid.NewGuid():N}";
        await ExecuteAsync(fixture.ConnectionString, $"CREATE DATABASE {database}", cancellationToken);
        try
        {
            var target = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = database }.ConnectionString;
            var options = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
            FamilyResilienceDbContextOptions.Configure(options, target);
            await using var context = new FamilyResilienceDbContext(options.Options);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(cancellationToken: cancellationToken);
            Assert.True(await TableExistsAsync(context, "assessment_responses", cancellationToken));

            await migrator.MigrateAsync("InitialCreate", cancellationToken);
            Assert.False(await TableExistsAsync(context, "households", cancellationToken));
            Assert.True(await TableExistsAsync(context, "audit_events", cancellationToken));

            await migrator.MigrateAsync(cancellationToken: cancellationToken);
            Assert.True(await TableExistsAsync(context, "households", cancellationToken));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(fixture.ConnectionString, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)", cancellationToken);
        }
    }

    private static async Task<bool> TableExistsAsync(FamilyResilienceDbContext context, string table, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<bool>($"SELECT to_regclass({table}) IS NOT NULL AS \"Value\"")
            .SingleAsync(cancellationToken);

    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
