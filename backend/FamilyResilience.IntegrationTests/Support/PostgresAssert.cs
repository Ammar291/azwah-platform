using Npgsql;

namespace FamilyResilience.IntegrationTests.Support;

internal static class PostgresAssert
{
    /// <summary>Asserts that PostgreSQL itself refused the statement, with the expected SQLSTATE and constraint.</summary>
    public static async Task<PostgresException> RejectsAsync(Func<Task> action, string sqlState, string? constraintName = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        if (constraintName is not null)
        {
            Assert.Equal(constraintName, exception.ConstraintName);
        }

        return exception;
    }
}
