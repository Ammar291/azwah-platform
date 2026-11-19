using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Demonstration;

public sealed class DemonstrationResetTests(DemonstrationResetDatabase database) : IClassFixture<DemonstrationResetDatabase>
{
    [Fact]
    public async Task Demo_creates_the_database_and_reset_wipes_what_was_added_and_rebuilds_the_same_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.False(await DemonstrationDatabase.ExistsAsync(database.Target, cancellationToken));

        Assert.Equal(0, await DemonstrationCommands.RunAsync(database.OrdinaryConnectionString, ["db", "demo"], cancellationToken));
        Assert.True(await DemonstrationDatabase.ExistsAsync(database.Target, cancellationToken));
        var built = await DemonstrationApi.RowCountsAsync(database.DemonstrationConnectionString, cancellationToken);

        // An audit event is append-only (no UPDATE or DELETE ever succeeds), so only dropping the database removes it.
        await using (var connection = new NpgsqlConnection(database.DemonstrationConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO audit_events (id, occurred_at, actor_type, action, resource_type, outcome) "
                + "VALUES (gen_random_uuid(), now(), 'System', 'tests.demonstration.used', 'demonstration_database', 'Succeeded')",
                connection);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        Assert.Equal(built["audit_events"] + 1, (await DemonstrationApi.RowCountsAsync(database.DemonstrationConnectionString, cancellationToken))["audit_events"]);

        Assert.Equal(0, await DemonstrationCommands.RunAsync(database.OrdinaryConnectionString, ["db", "demo", "--reset"], cancellationToken));

        Assert.Equal(built, await DemonstrationApi.RowCountsAsync(database.DemonstrationConnectionString, cancellationToken));
        Assert.Empty(await DemonstrationApi.RowCountsAsync(database.OrdinaryConnectionString, cancellationToken));
    }

    [Fact]
    public async Task An_unknown_option_is_refused_before_anything_is_created_or_dropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemonstrationCommands.RunAsync(database.OrdinaryConnectionString, ["db", "demo", "--drop-everything"], cancellationToken));

        Assert.Contains("db demo [--reset [--replace-foreign-database]]", exception.Message, StringComparison.Ordinal);
    }
}
