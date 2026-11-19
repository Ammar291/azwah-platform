using FamilyResilience.Api.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Runs a <c>db</c> command as <c>dotnet run -- db …</c> does in Development: only the configured ordinary connection
/// string and the clock (fixed at <see cref="TestData.Now"/>) are given; the command finds everything else itself.
/// </summary>
internal static class DemonstrationCommands
{
    public static async Task<int?> RunAsync(
        string ordinaryConnectionString, string[] args, CancellationToken cancellationToken, string environment = "Development")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ordinaryConnectionString })
            .Build();
        await using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now))
            .BuildServiceProvider();
        return await DatabaseCommands.TryRunAsync(args, services, new FakeHostEnvironment(environment), cancellationToken);
    }
}
