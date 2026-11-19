using FamilyResilience.Api.Database;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseCommandsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Ordinary_arguments_are_not_database_commands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Null(await DatabaseCommands.TryRunAsync([], fixture.Services, new FakeHostEnvironment("Development"), cancellationToken));
        Assert.Null(await DatabaseCommands.TryRunAsync(["--urls", "http://localhost:5080"], fixture.Services, new FakeHostEnvironment("Development"), cancellationToken));
    }

    [Fact]
    public async Task Migrate_applies_pending_migrations_and_returns_zero() =>
        Assert.Equal(
            0,
            await DatabaseCommands.TryRunAsync(["db", "migrate"], fixture.Services, new FakeHostEnvironment("Production"), TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Seed_refuses_outside_development(string environment)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseCommands.TryRunAsync(
            ["db", "seed"], fixture.Services, new FakeHostEnvironment(environment), TestContext.Current.CancellationToken));

        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Demo_refuses_outside_development_and_creates_no_database(string environment)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = DemonstrationDatabase.TargetFor(fixture.Services.GetRequiredService<IConfiguration>());

        foreach (var args in new[] { new[] { "db", "demo" }, ["db", "demo", "--reset"] })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseCommands.TryRunAsync(
                args, fixture.Services, new FakeHostEnvironment(environment), cancellationToken));

            Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
        }

        Assert.False(await DemonstrationDatabase.ExistsAsync(target, cancellationToken));
    }

    [Fact]
    public async Task Demonstration_data_is_refused_for_a_database_whose_name_does_not_end_in_demo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => DemonstrationDataSeeder.SeedAsync(fixture.Services, cancellationToken));

        Assert.Contains("'_demo'", exception.Message, StringComparison.Ordinal);
        Assert.False(await fixture.QueryAsync(db => db.Waves.AnyAsync(wave => wave.Code == DemonstrationDataSeeder.WaveOneCode, cancellationToken)));
        Assert.False(await fixture.QueryAsync(db => db.RuleSets.AnyAsync(
            rules => rules.Code == DemonstrationDataSeeder.EligibilityRuleSetCode, cancellationToken)));
    }

    [Fact]
    public async Task Unknown_database_command_is_rejected() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseCommands.TryRunAsync(
            ["db", "drop"], fixture.Services, new FakeHostEnvironment("Development"), TestContext.Current.CancellationToken));
}
