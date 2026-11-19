using FamilyResilience.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The demonstration database's name is derived, so the configured database is never the target (D-140).</summary>
public sealed class DemonstrationDatabaseTests
{
    private const string Ordinary = "Host=localhost;Port=5432;Database=azwah;Username=azwah;Password=local-only";

    private static IConfiguration Configuration(string? application, string? owner = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = application,
            ["ConnectionStrings:Migrations"] = owner,
        }).Build();

    private static string DatabaseOf(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Database!;

    [Fact]
    public void The_demonstration_database_is_the_configured_name_with_demo_appended_on_the_same_server()
    {
        var derived = new NpgsqlConnectionStringBuilder(DemonstrationDatabase.ConnectionStringFor(Ordinary));

        Assert.Equal(("azwah_demo", "localhost", 5432, "azwah", "local-only"),
            (derived.Database, derived.Host, derived.Port, derived.Username, derived.Password));
    }

    [Fact]
    public void A_configured_name_that_already_ends_in_demo_is_kept() =>
        Assert.Equal("azwah_demo", DatabaseOf(DemonstrationDatabase.ConnectionStringFor(Ordinary.Replace("azwah;", "azwah_demo;", StringComparison.Ordinal))));

    [Theory]
    [InlineData("Host=localhost;Username=azwah")]
    [InlineData("Host=localhost;Database=Azwah-Dev;Username=azwah")]
    [InlineData("Host=localhost;Database=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa;Username=azwah")]
    public void A_name_that_cannot_become_a_safe_demonstration_name_is_refused(string connectionString) =>
        Assert.Throws<InvalidOperationException>(() => DemonstrationDatabase.ConnectionStringFor(connectionString));

    [Theory]
    [InlineData("azwah_demo", true)]
    [InlineData("postgres_demo", true)]
    [InlineData("azwah", false)]
    [InlineData("_demo", false)]
    [InlineData("azwah_demo_copy", false)]
    [InlineData("Azwah_demo", false)]
    [InlineData("azwah\"; DROP DATABASE azwah; --_demo", false)]
    [InlineData(null, false)]
    public void Only_lower_case_names_ending_in_demo_are_demonstration_databases(string? name, bool expected) =>
        Assert.Equal(expected, DemonstrationDatabase.IsDemonstrationName(name));

    [Fact]
    public void The_owner_connection_is_derived_too_and_used_for_administration()
    {
        var owner = Ordinary.Replace("Username=azwah", "Username=owner", StringComparison.Ordinal);

        var target = DemonstrationDatabase.TargetFor(Configuration(Ordinary, owner));

        Assert.Equal("azwah_demo", target.DatabaseName);
        Assert.Equal("azwah_demo", DatabaseOf(target.ApplicationConnectionString));
        Assert.Equal(("azwah_demo", "owner"),
            (DatabaseOf(target.AdministrationConnectionString), new NpgsqlConnectionStringBuilder(target.AdministrationConnectionString).Username));
    }

    [Fact]
    public void Application_and_owner_connections_to_different_databases_are_refused() =>
        Assert.Throws<InvalidOperationException>(() => DemonstrationDatabase.TargetFor(
            Configuration(Ordinary, Ordinary.Replace("azwah;", "other;", StringComparison.Ordinal))));

    [Theory]
    [InlineData("Host=elsewhere.internal;Port=5432;Database=azwah;Username=owner;Password=local-only")]
    [InlineData("Host=localhost;Port=5433;Database=azwah;Username=owner;Password=local-only")]
    [InlineData("Host=localhost,elsewhere.internal;Port=5432;Database=azwah;Username=owner;Password=local-only")]
    public void Application_and_owner_connections_to_different_servers_are_refused_without_naming_them(string owner)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => DemonstrationDatabase.TargetFor(Configuration(Ordinary, owner)));

        Assert.Contains("same host and port", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("elsewhere", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("5433", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("local-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_port_written_out_is_the_same_server() =>
        Assert.Equal("azwah_demo", DemonstrationDatabase.TargetFor(Configuration(
            "Host=localhost;Database=azwah;Username=azwah;Password=local-only",
            "Host=localhost;Port=5432;Database=azwah;Username=owner;Password=local-only")).DatabaseName);

    /// <summary>Checked before any connection is opened, so no server is needed here.</summary>
    [Fact]
    public async Task A_target_built_by_hand_for_two_servers_cannot_be_inspected_created_or_dropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = new DemonstrationTarget(
            "azwah_demo",
            DemonstrationDatabase.ConnectionStringFor(Ordinary),
            DemonstrationDatabase.ConnectionStringFor(Ordinary.Replace("Host=localhost", "Host=elsewhere.internal", StringComparison.Ordinal)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DemonstrationDatabase.InspectAsync(target, cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemonstrationDatabase.CreateIfAbsentAsync(target, cancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemonstrationDatabase.DropAsync(target, cancellationToken));
    }

    [Fact]
    public void A_missing_default_connection_string_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => DemonstrationDatabase.TargetFor(Configuration(null)));

    [Theory]
    [InlineData("true", true)]
    [InlineData(" True ", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData(null, false)]
    public void The_setting_is_read_as_a_boolean(string? value, bool expected) =>
        Assert.Equal(expected, DemonstrationDatabase.IsRequested(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemonstrationDatabase.UseDemonstrationDatabaseSetting] = value })
            .Build()));
}
