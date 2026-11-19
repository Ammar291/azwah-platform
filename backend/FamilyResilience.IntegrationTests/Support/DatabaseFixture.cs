using FamilyResilience.Application;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Infrastructure;
using FamilyResilience.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// The Application and Infrastructure services against a throwaway PostgreSQL 18.6 container, migrated once. No web
/// host: persistence tests talk to repositories and the unit of work directly.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime, IScenarioDatabase
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();
    private ServiceProvider? _services;

    public string ConnectionString => _postgres.GetConnectionString();

    public IServiceProvider Services => _services ?? throw new InvalidOperationException("The fixture is not initialised.");

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _postgres.StartAsync(cancellationToken);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now));
        services.AddApplication();
        services.AddInfrastructure();
        services.AddScoped<ICurrentUser, FixtureCurrentUser>();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await DatabaseMigrator.MigrateAsync(_services, cancellationToken);
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Runs <paramref name="work"/> in a fresh scope and commits; a failed commit fails the test.</summary>
    public async Task InScopeAsync(Func<IServiceProvider, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = CreateScope();
        await work(scope.ServiceProvider);
        await CommitOrFailAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>Runs <paramref name="work"/> in a fresh scope, commits, and returns its value.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var scope = CreateScope();
        var value = await work(scope.ServiceProvider);
        await CommitOrFailAsync(scope.ServiceProvider, cancellationToken);
        return value;
    }

    /// <summary>Reads through a fresh context, so nothing is served from another scope's change tracker.</summary>
    public async Task<T> QueryAsync<T>(Func<FamilyResilienceDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>());
    }

    /// <summary>Runs raw SQL that bypasses the domain, to prove the database itself refuses bad data.</summary>
    public async Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        await using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>()
            .Database.ExecuteSqlAsync(sql, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _postgres.DisposeAsync();
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
