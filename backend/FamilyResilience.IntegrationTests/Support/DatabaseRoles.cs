using Npgsql;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>The shipped role scripts (Infrastructure/Persistence/Sql) and connections as those roles.</summary>
internal static class DatabaseRoles
{
    public const string Migrator = "azwah_migrator";
    public const string App = "azwah_app";
    public const string Password = "role-test-only";

    public static string Script(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyResilience.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("FamilyResilience.slnx was not found.");
        return File.ReadAllText(Path.Combine(root, "FamilyResilience.Infrastructure", "Persistence", "Sql", name));
    }

    public static string As(string connectionString, string role) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Username = role, Password = Password }.ConnectionString;

    public static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Creates both roles with the test password (as a superuser) and grants the application role (as the owner).</summary>
    public static async Task SetUpAsync(string superuserConnectionString, string ownerConnectionString, CancellationToken cancellationToken)
    {
        await ExecuteAsync(superuserConnectionString, Script("database-roles.sql"), cancellationToken);
        await ExecuteAsync(
            superuserConnectionString,
            $"ALTER ROLE {Migrator} PASSWORD '{Password}'; ALTER ROLE {App} PASSWORD '{Password}';",
            cancellationToken);
        await ExecuteAsync(ownerConnectionString, Script("application-role.sql"), cancellationToken);
    }
}
