using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>
/// A database with the demonstration's name that already exists but holds nothing (D-140): one created by hand and never
/// migrated, or one migrated and never seeded. Neither can lose data, so <c>db demo</c> builds into it as into a new one.
/// </summary>
public sealed class DemonstrationEmptyDatabaseTests(DemonstrationServer server) : IClassFixture<DemonstrationServer>
{
    [Fact]
    public async Task Demo_builds_into_an_existing_database_that_was_created_but_never_migrated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = server.Target("created_only");
        Assert.True(await DemonstrationDatabase.CreateIfAbsentAsync(target, cancellationToken));
        Assert.Equal(DemonstrationDatabaseState.Empty, await DemonstrationDatabase.InspectAsync(target, cancellationToken));

        Assert.Equal(0, await server.RunAsync("created_only", ["db", "demo"], cancellationToken));

        Assert.True(await DemonstrationServer.IsBuiltAsync(target, cancellationToken));
        Assert.Equal(DemonstrationDatabaseState.Demonstration, await DemonstrationDatabase.InspectAsync(target, cancellationToken));
    }

    [Fact]
    public async Task Demo_builds_into_a_migrated_database_that_holds_no_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("migrated_only", null, cancellationToken);
        Assert.Equal(DemonstrationDatabaseState.Empty, await DemonstrationDatabase.InspectAsync(target, cancellationToken));

        Assert.Equal(0, await server.RunAsync("migrated_only", ["db", "demo"], cancellationToken));

        Assert.True(await DemonstrationServer.IsBuiltAsync(target, cancellationToken));
    }

    [Fact]
    public async Task A_database_that_does_not_exist_is_absent_and_one_with_rows_but_no_marker_is_foreign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(DemonstrationDatabaseState.Absent, await DemonstrationDatabase.InspectAsync(server.Target("never_made"), cancellationToken));

        var target = await server.MigratedAsync("rows_only", DemonstrationServer.OlderMigration, cancellationToken);
        Assert.Equal(DemonstrationDatabaseState.Empty, await DemonstrationDatabase.InspectAsync(target, cancellationToken));
        // A table of the owner's own, outside the platform's schema, counts as much as a platform table.
        await DemonstrationServer.ScalarAsync(target, "CREATE SCHEMA owner; CREATE TABLE owner.notes (body text); INSERT INTO owner.notes VALUES ('kept')", cancellationToken);
        Assert.Equal(DemonstrationDatabaseState.Foreign, await DemonstrationDatabase.InspectAsync(target, cancellationToken));
        await DemonstrationServer.InsertMarkerAsync(target, cancellationToken);
        Assert.Equal(DemonstrationDatabaseState.Demonstration, await DemonstrationDatabase.InspectAsync(target, cancellationToken));
    }
}
