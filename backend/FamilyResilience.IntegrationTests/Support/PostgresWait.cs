using FamilyResilience.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Lets a test hold a lock until it knows another connection is waiting on it, so races run in a fixed order.</summary>
internal static class PostgresWait
{
    /// <summary>
    /// Polls PostgreSQL until a backend is blocked by this context's connection, or fails after ten seconds. The caller is
    /// usually inside a transaction, where PostgreSQL keeps the first copy of <c>pg_stat_activity</c> it read, so each
    /// poll clears that snapshot first; otherwise a connection opened after the first poll would never be seen.
    /// </summary>
    public static async Task UntilAnotherConnectionWaitsOnAsync(FamilyResilienceDbContext db, CancellationToken cancellationToken)
    {
        var pid = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync(cancellationToken);
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_stat_clear_snapshot()", cancellationToken);
            var waiting = await db.Database
                .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE {pid} = ANY(pg_blocking_pids(pid))")
                .SingleAsync(cancellationToken);
            if (waiting > 0)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        Assert.Fail("No other connection waited on the lock this test holds.");
    }
}
