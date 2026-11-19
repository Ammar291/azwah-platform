using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// The API as a deployed environment (Production) sees it: real JwtBearer, no test endpoints, safe deployed settings by
/// default, and no database (nothing listens on port 1). <paramref name="overrides"/> replaces or removes (null) one
/// setting to break it.
/// </summary>
public sealed class DeployedApiFactory(IReadOnlyDictionary<string, string?>? overrides = null) : WebApplicationFactory<Program>
{
    public const string PublicHost = "api.azwah.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=2;SSL Mode=Require",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["BackgroundJobs:Enabled"] = "false",
                ["Authentication:Schemes:Bearer:Authority"] = "https://issuer.azwah.test",
                ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "azwah-api",
                ["Cors:AllowedOrigins:0"] = "https://admin.azwah.test",
            };
            foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            configuration.AddInMemoryCollection(settings);
        });
    }

    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"https://{PublicHost}") });

    public HttpClient CreatePlainHttpClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{PublicHost}") });
}
