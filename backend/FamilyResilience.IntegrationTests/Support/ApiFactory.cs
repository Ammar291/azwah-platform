using FamilyResilience.Application.Abstractions;
using FamilyResilience.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>The API against a throwaway PostgreSQL 18.6 container, migrated on startup.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime, IScenarioDatabase
{
    public const string AllowedOrigin = "http://localhost:3000";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    /// <summary>A throwaway directory for export files, deleted with the host.</summary>
    public string ExportDirectory { get; } = Path.Combine(Path.GetTempPath(), "azwah-test-exports", Guid.NewGuid().ToString("N"));

    public async ValueTask InitializeAsync() => await _postgres.StartAsync(TestContext.Current.CancellationToken);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Added last, so these win over appsettings, user secrets and environment variables. The API reads its
        // configuration only after Build(), when these sources are in place.
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Cors:AllowedOrigins:0"] = AllowedOrigin,
                ["BackgroundJobs:Enabled"] = "false",
                // Shared hosts send hundreds of requests per subject a minute; RateLimitingTests runs a host with limits on.
                ["RateLimiting:Enabled"] = "false",
                ["Exports:LocalDirectory"] = ExportDirectory,
            }));
        builder.ConfigureTestServices(TestServices.Configure);
    }

    public async Task InScopeAsync(Func<IServiceProvider, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Services.CreateAsyncScope();
        await work(scope.ServiceProvider);
        await CommitOrFailAsync(scope.ServiceProvider, cancellationToken);
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = Services.CreateAsyncScope();
        var value = await work(scope.ServiceProvider);
        await CommitOrFailAsync(scope.ServiceProvider, cancellationToken);
        return value;
    }

    public async Task<T> QueryAsync<T>(Func<FamilyResilienceDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }

    private static async Task CommitOrFailAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var committed = await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            throw new InvalidOperationException($"Test setup could not commit: {committed.Error!.Code}");
        }
    }
}
