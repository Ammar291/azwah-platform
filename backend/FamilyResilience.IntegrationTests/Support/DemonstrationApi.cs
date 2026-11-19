using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// The API against a demonstration database of its own (D-140), built the way the owner builds it: <c>db demo</c> in the
/// Development environment against an ordinary database named <c>azwah</c>, which derives, creates, migrates and seeds
/// <c>azwah_demo</c> in the same container. The host then runs against <c>azwah_demo</c> with the test clock, so the
/// timeline is relative to <see cref="TestData.Now"/>. Nothing else uses this container, so the shared collections'
/// exact counts are untouched.
/// </summary>
public sealed class DemonstrationApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OrdinaryDatabase = "azwah";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").WithDatabase(OrdinaryDatabase).Build();

    public string OrdinaryConnectionString => _postgres.GetConnectionString();

    public string DemonstrationConnectionString => DemonstrationDatabase.ConnectionStringFor(OrdinaryConnectionString);

    public Wave WaveOne { get; private set; } = null!;

    public Wave WaveTwo { get; private set; } = null!;

    /// <summary>
    /// What the build wrote, counted before any test runs (the persona tests answer and enrol live afterwards): keys such
    /// as <c>households.active</c> or <c>demo_wave_1.assignments.expired</c>, statuses in snake case.
    /// </summary>
    public IReadOnlyDictionary<string, int> AsBuilt { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _postgres.StartAsync(cancellationToken);
        Assert.Equal(0, await RunDemoCommandAsync(["db", "demo"], cancellationToken));
        WaveOne = await QueryAsync(db => db.Waves.AsNoTracking().SingleAsync(wave => wave.Code == DemonstrationDataSeeder.WaveOneCode, cancellationToken));
        WaveTwo = await QueryAsync(db => db.Waves.AsNoTracking().SingleAsync(wave => wave.Code == DemonstrationDataSeeder.WaveTwoCode, cancellationToken));
        AsBuilt = await QueryAsync(db => CountAsync(db, cancellationToken));
    }

    private static async Task<IReadOnlyDictionary<string, int>> CountAsync(FamilyResilienceDbContext db, CancellationToken cancellationToken)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["households"] = await db.Households.CountAsync(cancellationToken),
            ["households.roster_confirmed"] = await db.Households.CountAsync(household => household.RosterConfirmedAt != null, cancellationToken),
            ["members"] = await db.Set<FamilyMember>().CountAsync(cancellationToken),
        };
        foreach (var group in await db.Households.GroupBy(household => household.Status)
            .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
        {
            counts[$"households.{Snake(group.Key)}"] = group.Count;
        }

        foreach (var wave in await db.Waves.AsNoTracking().ToListAsync(cancellationToken))
        {
            var assignments = db.AssessmentAssignments.Where(assignment => assignment.WaveId == wave.Id);
            var sessions = db.AssessmentSessions.Join(assignments, session => session.AssessmentAssignmentId, assignment => assignment.Id, (session, _) => session);
            counts[$"{wave.Code}.assignments"] = await assignments.CountAsync(cancellationToken);
            foreach (var group in await assignments.GroupBy(assignment => assignment.Status)
                .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
            {
                counts[$"{wave.Code}.assignments.{Snake(group.Key)}"] = group.Count;
            }

            counts[$"{wave.Code}.sessions"] = await sessions.CountAsync(cancellationToken);
            foreach (var group in await sessions.GroupBy(session => session.Status)
                .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken))
            {
                counts[$"{wave.Code}.sessions.{Snake(group.Key)}"] = group.Count;
            }

            counts[$"{wave.Code}.results"] = await db.ScoreResults
                .Join(sessions, result => result.AssessmentSessionId, session => session.Id, (result, _) => result)
                .CountAsync(cancellationToken);
        }

        return counts;
    }

    private static string Snake<T>(T status) where T : struct, Enum =>
        string.Concat(status.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    /// <summary>Runs a <c>db</c> command in Development with the ordinary database configured, as <c>npm run demo:db</c> does.</summary>
    public Task<int?> RunDemoCommandAsync(string[] args, CancellationToken cancellationToken) =>
        DemonstrationCommands.RunAsync(OrdinaryConnectionString, args, cancellationToken);

    /// <summary>A client signed in through the test scheme with the subject and roles a persona's token would carry.</summary>
    public HttpClient As(string subject, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = DemonstrationConnectionString,
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Cors:AllowedOrigins:0"] = ApiFactory.AllowedOrigin,
                ["BackgroundJobs:Enabled"] = "false",
                ["RateLimiting:Enabled"] = "false",
            }));
        builder.ConfigureTestServices(TestServices.Configure);
    }

    public async Task<T> QueryAsync<T>(Func<FamilyResilienceDbContext, Task<T>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FamilyResilienceDbContext>());
    }

    /// <summary>Rows per table of a database, to show that something changed nothing (or the same thing again).</summary>
    public static async Task<IReadOnlyDictionary<string, long>> RowCountsAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY table_name",
            connection))
        await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            counts[table] = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        return counts;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
