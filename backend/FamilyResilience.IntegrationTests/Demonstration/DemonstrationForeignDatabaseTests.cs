using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>
/// A database with the demonstration's name that <c>db demo</c> did not make (D-140): the owner's own <c>azwah_demo</c>,
/// hand-made on another branch. It is inspected before anything drops or migrates it, and replaced only on request.
/// </summary>
public sealed class DemonstrationForeignDatabaseTests(DemonstrationServer server) : IClassFixture<DemonstrationServer>
{
    private const string OwnersHousehold = "HH-0WN1-0001";

    private static void AssertRefusedAsForeign(InvalidOperationException exception, string database)
    {
        Assert.Contains($"'{database}' was not made by 'db demo'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("left untouched", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'db demo --reset --replace-foreign-database'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_refuses_a_demo_database_holding_rows_the_seeder_did_not_write_and_leaves_every_row_intact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("azwah", null, cancellationToken);
        await using (var db = DemonstrationServer.Context(target))
        {
            db.Households.Add(TestData.NewHousehold(TestData.Head(), TestData.Spouse()));
            await db.SaveChangesAsync(cancellationToken);
        }

        var before = await DemonstrationServer.SnapshotAsync(target, cancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.RunAsync("azwah", ["db", "demo", "--reset"], cancellationToken));

        AssertRefusedAsForeign(exception, "azwah_demo");
        Assert.True(await DemonstrationDatabase.ExistsAsync(target, cancellationToken));
        Assert.Equal(before, await DemonstrationServer.SnapshotAsync(target, cancellationToken));
        Assert.Contains("rows households 1", before, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_refuses_a_foreign_database_on_an_older_migration_without_migrating_or_writing_to_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("owner_b", DemonstrationServer.OlderMigration, cancellationToken);
        await DemonstrationServer.InsertForeignHouseholdAsync(target, OwnersHousehold, cancellationToken);
        var before = await DemonstrationServer.SnapshotAsync(target, cancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.RunAsync("owner_b", ["db", "demo"], cancellationToken));

        AssertRefusedAsForeign(exception, "owner_b_demo");
        Assert.Equal(before, await DemonstrationServer.SnapshotAsync(target, cancellationToken));
        Assert.EndsWith($"_{DemonstrationServer.OlderMigration}", before.Split('\n').Last(line => line.StartsWith("migration ", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.True(await DemonstrationServer.HasHouseholdAsync(target, OwnersHousehold, cancellationToken));
    }

    /// <summary>The configured database is the target when its name already ends in <c>_demo</c>; it gets no exemption.</summary>
    [Fact]
    public async Task A_configured_database_whose_name_ends_in_demo_is_its_own_target_and_is_refused_like_any_other()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("owners_demo", DemonstrationServer.OlderMigration, cancellationToken);
        Assert.Equal("owners_demo", target.DatabaseName);
        await DemonstrationServer.InsertForeignHouseholdAsync(target, OwnersHousehold, cancellationToken);
        var before = await DemonstrationServer.SnapshotAsync(target, cancellationToken);

        foreach (var args in new[] { new[] { "db", "demo" }, ["db", "demo", "--reset"] })
        {
            AssertRefusedAsForeign(
                await Assert.ThrowsAsync<InvalidOperationException>(() => server.RunAsync("owners_demo", args, cancellationToken)),
                "owners_demo");
        }

        Assert.Equal(before, await DemonstrationServer.SnapshotAsync(target, cancellationToken));
    }

    [Fact]
    public async Task The_explicit_opt_in_replaces_a_foreign_database_with_the_demonstration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("owner_c", DemonstrationServer.OlderMigration, cancellationToken);
        await DemonstrationServer.InsertForeignHouseholdAsync(target, OwnersHousehold, cancellationToken);

        Assert.Equal(0, await server.RunAsync(
            "owner_c", ["db", "demo", "--reset", "--replace-foreign-database"], cancellationToken));

        Assert.False(await DemonstrationServer.HasHouseholdAsync(target, OwnersHousehold, cancellationToken));
        Assert.True(await DemonstrationServer.IsBuiltAsync(target, cancellationToken));
    }

    /// <summary>A demonstration built on an older branch carries the marker, so a reset drops it whatever its migrations.</summary>
    [Fact]
    public async Task Reset_drops_a_marked_demonstration_database_on_an_older_migration_and_rebuilds_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = await server.MigratedAsync("older_branch", DemonstrationServer.OlderMigration, cancellationToken);
        await DemonstrationServer.InsertForeignHouseholdAsync(target, OwnersHousehold, cancellationToken);
        await DemonstrationServer.InsertMarkerAsync(target, cancellationToken);

        Assert.Equal(0, await server.RunAsync("older_branch", ["db", "demo", "--reset"], cancellationToken));

        Assert.False(await DemonstrationServer.HasHouseholdAsync(target, OwnersHousehold, cancellationToken));
        Assert.True(await DemonstrationServer.IsBuiltAsync(target, cancellationToken));
    }

    [Fact]
    public async Task The_replace_option_is_refused_without_reset_before_anything_is_created()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.RunAsync("owner_d", ["db", "demo", "--replace-foreign-database"], cancellationToken));

        Assert.Contains("db demo [--reset [--replace-foreign-database]]", exception.Message, StringComparison.Ordinal);
        Assert.False(await DemonstrationDatabase.ExistsAsync(server.Target("owner_d"), cancellationToken));
    }
}
