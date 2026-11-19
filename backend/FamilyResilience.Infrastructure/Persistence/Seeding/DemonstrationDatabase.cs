using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// The guard around the local demonstration database (D-140). Demonstration data lives only in a database whose name
/// ends in <c>_demo</c>, derived from the configured one: <c>azwah</c> becomes <c>azwah_demo</c>, and a name that
/// already ends in <c>_demo</c> is kept, so then the configured database is itself the target. Either way the target is
/// inspected (<see cref="InspectAsync"/>) before anything drops, migrates or seeds it, and only an absent or empty
/// database, or one carrying the marker of a completed build, is used without the explicit replace option. Creating and
/// dropping use the schema owner's connection (<c>ConnectionStrings:Migrations</c> when set, on the same host and port
/// as <c>Default</c>) through the cluster's <c>postgres</c> database. Development only: the API command and the API
/// setting refuse every other environment.
/// </summary>
public static partial class DemonstrationDatabase
{
    public const string Suffix = "_demo";

    /// <summary>Development only: run the API against the demonstration database instead of the configured one.</summary>
    public const string UseDemonstrationDatabaseSetting = "Database:UseDemonstrationDatabase";

    /// <summary>The resource type of the marker event (<see cref="DemonstrationDataSeeder.SeededAuditAction"/>).</summary>
    public const string MarkerResourceType = "demonstration_database";

    private const string MaintenanceDatabase = "postgres";
    private const string DuplicateDatabase = "42P04";

    private const string DifferentServers =
        "ConnectionStrings:Default and ConnectionStrings:Migrations point at different servers, so the demonstration database "
        + "would be created or dropped on one and used on the other. Give both the same host and port.";

    /// <summary>Whether <c>Database:UseDemonstrationDatabase</c> is true, read as the start-up guard reads switches.</summary>
    public static bool IsRequested(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return bool.TryParse(configuration[UseDemonstrationDatabaseSetting]?.Trim(), out var requested) && requested;
    }

    /// <summary>Lower-case letters, digits and underscores, at most 63 characters, ending in <c>_demo</c>.</summary>
    public static bool IsDemonstrationName(string? name) =>
        name is not null && name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal) && SafeName().IsMatch(name);

    /// <summary>
    /// Whether the connection string names a database ending in <c>_demo</c>, in any letter case. The deployed-settings
    /// guard refuses such a connection (D-140), so a deployed API never runs against a demonstration database however it
    /// was pointed there. A name that only contains <c>demo</c> elsewhere is not one.
    /// </summary>
    public static bool NamesDemonstrationDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var given = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var database = new[] { "Database", "DB" }
            .Select(key => given.TryGetValue(key, out var value) ? value?.ToString() : null)
            .FirstOrDefault(value => value is not null);
        return database?.Trim().EndsWith(Suffix, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>The connection string with its database replaced by the demonstration database.</summary>
    public static string ConnectionStringFor(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException(
                "The connection string names no database, so the demonstration database cannot be derived from it.");
        }

        var name = builder.Database.EndsWith(Suffix, StringComparison.Ordinal) ? builder.Database : builder.Database + Suffix;
        if (!IsDemonstrationName(name))
        {
            throw new InvalidOperationException(
                "The demonstration database is named after the configured database plus '_demo', which must be lower-case "
                + "letters, digits and underscores (at most 63 characters).");
        }

        builder.Database = name;
        return builder.ConnectionString;
    }

    /// <summary>The demonstration database for the configured <c>Default</c> (and <c>Migrations</c>) connection strings.</summary>
    public static DemonstrationTarget TargetFor(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var application = configuration.GetConnectionString(FamilyResilienceDbContextOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(application))
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");
        }

        var target = ConnectionStringFor(application);
        var owner = configuration.GetConnectionString(DatabaseMigrator.MigrationsConnectionStringName);
        var ownerTarget = string.IsNullOrWhiteSpace(owner) ? null : ConnectionStringFor(owner);
        var name = DatabaseOf(target);
        if (ownerTarget is not null && DatabaseOf(ownerTarget) != name)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Default and ConnectionStrings:Migrations name different databases, so they do not agree on "
                + "one demonstration database.");
        }

        if (ownerTarget is not null && !SameServer(target, ownerTarget))
        {
            throw new InvalidOperationException(DifferentServers);
        }

        return new DemonstrationTarget(name, target, ownerTarget);
    }

    /// <summary>
    /// What the demonstration database holds, read before anything drops, migrates or seeds it, through the schema
    /// owner's connection and only with queries. It is a <see cref="DemonstrationDatabaseState.Demonstration"/> when its
    /// audit trail has the event a completed build writes (<see cref="DemonstrationDataSeeder.SeededAuditAction"/> by
    /// <see cref="DemonstrationDataSeeder.SeedSubject"/>): <c>audit_events</c> and those columns exist from the first
    /// migration, so the marker is read on any migration. Otherwise it is
    /// <see cref="DemonstrationDatabaseState.Empty"/> when no table in any of its schemas holds a row (EF Core's
    /// migration history aside) and it has no large object, and <see cref="DemonstrationDatabaseState.Foreign"/> when
    /// anything holds data or cannot be read.
    /// </summary>
    public static async Task<DemonstrationDatabaseState> InspectAsync(DemonstrationTarget target, CancellationToken cancellationToken = default)
    {
        var name = Checked(target);
        await using (var maintenance = new NpgsqlConnection(MaintenanceConnectionString(target)))
        {
            await maintenance.OpenAsync(cancellationToken);
            if (!await ExistsAsync(maintenance, name, cancellationToken))
            {
                return DemonstrationDatabaseState.Absent;
            }
        }

        var inspection = new NpgsqlConnectionStringBuilder(FamilyResilienceDbContextOptions.WithConnectionDefaults(target.AdministrationConnectionString))
        {
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(inspection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (await HasMarkerAsync(connection, cancellationToken))
        {
            return DemonstrationDatabaseState.Demonstration;
        }

        return await HoldsDataAsync(connection, cancellationToken) ? DemonstrationDatabaseState.Foreign : DemonstrationDatabaseState.Empty;
    }

    /// <summary>Creates the database unless it exists. Returns whether it was created.</summary>
    public static async Task<bool> CreateIfAbsentAsync(DemonstrationTarget target, CancellationToken cancellationToken = default)
    {
        var name = Checked(target);
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString(target));
        await connection.OpenAsync(cancellationToken);
        if (await ExistsAsync(connection, name, cancellationToken))
        {
            return false;
        }

        try
        {
            // An identifier cannot be a parameter; the name is checked against SafeName above.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == DuplicateDatabase)
        {
            return false;
        }
    }

    /// <summary>
    /// Drops the demonstration database and everything in it, ending its open connections first. Audit events are
    /// append-only and approved content is immutable, so a demonstration is wiped this way, never row by row.
    /// </summary>
    public static async Task DropAsync(DemonstrationTarget target, CancellationToken cancellationToken = default)
    {
        var name = Checked(target);
        ClearPools(target);
        await using (var connection = new NpgsqlConnection(MaintenanceConnectionString(target)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        ClearPools(target);
    }

    /// <summary>Whether the demonstration database exists.</summary>
    public static async Task<bool> ExistsAsync(DemonstrationTarget target, CancellationToken cancellationToken = default)
    {
        var name = Checked(target);
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString(target));
        await connection.OpenAsync(cancellationToken);
        return await ExistsAsync(connection, name, cancellationToken);
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", name);
        return await exists.ExecuteScalarAsync(cancellationToken) is not null;
    }

    /// <summary>The marker of a completed build, in the platform's audit table if this database has one.</summary>
    private static async Task<bool> HasMarkerAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using (var columns = new NpgsqlCommand(
            "SELECT count(*) FROM pg_attribute WHERE attrelid = to_regclass('public.audit_events') "
            + "AND attname = ANY(@columns) AND attnum > 0 AND NOT attisdropped",
            connection))
        {
            columns.Parameters.AddWithValue("columns", new[] { "action", "resource_type", "actor_id" });
            if (await columns.ExecuteScalarAsync(cancellationToken) is not long count || count != 3)
            {
                return false;
            }
        }

        try
        {
            await using var marker = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM public.audit_events WHERE action = @action AND resource_type = @type AND actor_id = @actor)",
                connection);
            marker.Parameters.AddWithValue("action", DemonstrationDataSeeder.SeededAuditAction);
            marker.Parameters.AddWithValue("type", MarkerResourceType);
            marker.Parameters.AddWithValue("actor", DemonstrationDataSeeder.SeedSubject);
            return await marker.ExecuteScalarAsync(cancellationToken) is true;
        }
        catch (PostgresException)
        {
            // An audit table of another shape, or one this role cannot read: no marker can be seen.
            return false;
        }
    }

    /// <summary>
    /// Whether any table, partitioned table or materialized view in any schema holds a row, EF Core's migration history
    /// aside, or any large object exists. A relation that cannot be read counts as holding data.
    /// </summary>
    private static async Task<bool> HoldsDataAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var relations = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT format('%I.%I', n.nspname, c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
            + "WHERE c.relkind IN ('r', 'p', 'm') AND n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg\\_%' "
            + "AND NOT (n.nspname = 'public' AND c.relname = '__EFMigrationsHistory') ORDER BY 1",
            connection))
        await using (var reader = await list.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                relations.Add(reader.GetString(0));
            }
        }

        // The names come quoted from the server (format's %I), so they are safe as identifiers.
        foreach (var query in relations.Select(relation => $"SELECT EXISTS (SELECT 1 FROM {relation})")
            .Append("SELECT EXISTS (SELECT 1 FROM pg_largeobject_metadata)"))
        {
            try
            {
                await using var rows = new NpgsqlCommand(query, connection);
                if (await rows.ExecuteScalarAsync(cancellationToken) is not false)
                {
                    return true;
                }
            }
            catch (PostgresException)
            {
                return true;
            }
        }

        return false;
    }

    private static string Checked(DemonstrationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!IsDemonstrationName(target.DatabaseName)
            || DatabaseOf(target.ApplicationConnectionString) != target.DatabaseName
            || (target.OwnerConnectionString is { } owner && DatabaseOf(owner) != target.DatabaseName))
        {
            throw new InvalidOperationException("Only a database whose name ends in '_demo' can be created or dropped as the demonstration database.");
        }

        if (target.OwnerConnectionString is { } ownerConnection && !SameServer(target.ApplicationConnectionString, ownerConnection))
        {
            throw new InvalidOperationException(DifferentServers);
        }

        return target.DatabaseName;
    }

    private static string DatabaseOf(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;

    /// <summary>The same host list, in the same order, and the same port: anything else is treated as another server.</summary>
    private static bool SameServer(string first, string second)
    {
        var one = new NpgsqlConnectionStringBuilder(first);
        var other = new NpgsqlConnectionStringBuilder(second);
        return one.Port == other.Port && Hosts(one).SequenceEqual(Hosts(other), StringComparer.Ordinal);

        static string[] Hosts(NpgsqlConnectionStringBuilder builder) =>
            (builder.Host ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
    }

    private static string MaintenanceConnectionString(DemonstrationTarget target) =>
        new NpgsqlConnectionStringBuilder(FamilyResilienceDbContextOptions.WithConnectionDefaults(target.AdministrationConnectionString))
        {
            Database = MaintenanceDatabase,
            Pooling = false,
        }.ConnectionString;

    /// <summary>Idle pooled connections to a dropped database are dead; the pools are keyed by the exact string used.</summary>
    private static void ClearPools(DemonstrationTarget target)
    {
        foreach (var connectionString in new[] { target.ApplicationConnectionString, target.OwnerConnectionString }.OfType<string>())
        {
            foreach (var variant in new[] { connectionString, FamilyResilienceDbContextOptions.WithConnectionDefaults(connectionString) })
            {
                using var connection = new NpgsqlConnection(variant);
                NpgsqlConnection.ClearPool(connection);
            }
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
