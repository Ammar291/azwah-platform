using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Demonstration;

/// <summary>A container of its own, because <c>db demo --reset</c> drops the demonstration database under any host using it.</summary>
public sealed class DemonstrationResetDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").WithDatabase(DemonstrationApi.OrdinaryDatabase).Build();

    public string OrdinaryConnectionString => _postgres.GetConnectionString();

    public string DemonstrationConnectionString => DemonstrationDatabase.ConnectionStringFor(OrdinaryConnectionString);

    public DemonstrationTarget Target => DemonstrationDatabase.TargetFor(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = OrdinaryConnectionString })
        .Build());

    public async ValueTask InitializeAsync() => await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}
