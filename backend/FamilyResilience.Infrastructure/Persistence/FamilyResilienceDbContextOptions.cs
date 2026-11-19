using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyResilience.Infrastructure.Persistence;

/// <summary>One place that configures the provider, so the app, `dotnet ef` and tests build the same model.</summary>
public static class FamilyResilienceDbContextOptions
{
    public const string ConnectionStringName = "Default";

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        options
            .UseNpgsql(WithConnectionDefaults(connectionString))
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging(false);
    }

    /// <summary>
    /// Platform defaults for every PostgreSQL connection. GSS (Kerberos) encryption is off unless the connection string
    /// asks for it: the platform does not use Kerberos, and Npgsql would otherwise probe for libgssapi on each connection.
    /// </summary>
    public static string WithConnectionDefaults(string connectionString)
    {
        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (given.ContainsKey("GSS Encryption Mode") || given.ContainsKey("GssEncryptionMode"))
        {
            return connectionString;
        }

        return new NpgsqlConnectionStringBuilder(connectionString) { GssEncryptionMode = GssEncryptionMode.Disable }
            .ConnectionString;
    }

    /// <summary>
    /// Whether the connection string makes TLS mandatory (<c>SSL Mode</c> Require, VerifyCA or VerifyFull). Npgsql's
    /// default, Prefer, silently falls back to plain text, so deployed hosts must not rely on it.
    /// </summary>
    public static bool RequiresTls(string connectionString)
    {
        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var mode = new[] { "SSL Mode", "SslMode", "sslmode" }
            .Select(key => given.TryGetValue(key, out var value) ? value?.ToString() : null)
            .FirstOrDefault(value => value is not null);
        var normalized = mode?.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized is "REQUIRE" or "VERIFYCA" or "VERIFYFULL";
    }
}
