using FamilyResilience.Api.Database;
using FamilyResilience.UnitTests.Support;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FamilyResilience.UnitTests.Api;

/// <summary><c>Database:UseDemonstrationDatabase</c> points the API at <c>&lt;name&gt;_demo</c>, in Development only (D-140).</summary>
public sealed class DemonstrationDatabaseSettingTests
{
    private const string Ordinary = "Host=localhost;Database=azwah;Username=azwah;Password=local-only";

    private static ConfigurationManager Configuration(string? setting, string? owner = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = Ordinary,
            ["ConnectionStrings:Migrations"] = owner,
            ["Database:UseDemonstrationDatabase"] = setting,
        });
        return configuration;
    }

    private static string? DatabaseOf(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name) is { } connectionString ? new NpgsqlConnectionStringBuilder(connectionString).Database : null;

    [Fact]
    public void In_development_the_api_uses_the_demonstration_database_for_both_connections()
    {
        var configuration = Configuration("true", owner: Ordinary.Replace("Username=azwah", "Username=owner", StringComparison.Ordinal));

        DemonstrationDatabaseSetting.Apply(configuration, new StubHostEnvironment("Development"));

        Assert.Equal(("azwah_demo", "azwah_demo"), (DatabaseOf(configuration, "Default"), DatabaseOf(configuration, "Migrations")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Without_the_setting_nothing_changes(string? setting)
    {
        var configuration = Configuration(setting);

        DemonstrationDatabaseSetting.Apply(configuration, new StubHostEnvironment("Production"));

        Assert.Equal("azwah", DatabaseOf(configuration, "Default"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void The_setting_is_refused_outside_development(string environment)
    {
        var configuration = Configuration("true");

        var exception = Assert.Throws<InvalidOperationException>(() => DemonstrationDatabaseSetting.Apply(configuration, new StubHostEnvironment(environment)));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
        Assert.Equal("azwah", DatabaseOf(configuration, "Default"));
    }
}
