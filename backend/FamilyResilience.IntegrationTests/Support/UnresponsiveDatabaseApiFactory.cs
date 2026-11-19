using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>The API pointed at a database host that accepts connections but never answers.</summary>
public sealed class UnresponsiveDatabaseApiFactory(int port) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // Npgsql's own connect timeout (15 s) is longer than readiness may take.
                ["ConnectionStrings:Default"] =
                    $"Host=127.0.0.1;Port={port};Database=silent;Username=silent;Password=silent;Timeout=15",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                // The dispatcher and maintenance service would open their own hanging connections; this is about readiness.
                ["BackgroundJobs:Enabled"] = "false",
            }));
    }
}
