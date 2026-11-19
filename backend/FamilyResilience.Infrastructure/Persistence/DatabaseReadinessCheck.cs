using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>
/// Readiness: can the API open a connection to PostgreSQL and run a trivial query? The whole check has one deadline of
/// <see cref="TimeoutSeconds"/>: Npgsql's own connect and command timeouts back it up (Npgsql does not stop waiting on
/// a host that accepts the connection but never replies), and a cancelled query does not wait for the server to
/// acknowledge the cancellation. A health check must not hang a probe.
/// </summary>
public sealed class DatabaseReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    public const int TimeoutSeconds = 3;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connectionString = configuration.GetConnectionString(FamilyResilienceDbContextOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "No database connection string.");
        }

        var builder = new NpgsqlConnectionStringBuilder(FamilyResilienceDbContextOptions.WithConnectionDefaults(connectionString))
        {
            Timeout = TimeoutSeconds,
            CommandTimeout = TimeoutSeconds,
            // -1: after a timeout or cancellation, do not wait for the server to confirm it; the connection is dropped.
            CancellationTimeout = -1,
        };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(deadline.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(deadline.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database did not answer in time.", exception);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            // The exception is logged by the health check service; only the status reaches the client.
            return new HealthCheckResult(context.Registration.FailureStatus, "The database is not reachable.", exception);
        }
    }
}
