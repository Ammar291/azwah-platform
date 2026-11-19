using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>The API pointed at a port where nothing listens, to see how it behaves when the database is down.</summary>
public sealed class NoDatabaseApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // Nothing listens on port 1, so every connection attempt is refused at once.
                ["ConnectionStrings:Default"] =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=2",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                // The dispatcher would poll the missing database and log every failure; these tests are about requests.
                ["BackgroundJobs:Enabled"] = "false",
            }));
    }
}
