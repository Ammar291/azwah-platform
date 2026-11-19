using FamilyResilience.Infrastructure.Persistence;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>A database that scenario helpers can write to (one committed scope per step) and read from.</summary>
public interface IScenarioDatabase
{
    Task InScopeAsync(Func<IServiceProvider, Task> work, CancellationToken cancellationToken);

    Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken);

    Task<T> QueryAsync<T>(Func<FamilyResilienceDbContext, Task<T>> query);
}
