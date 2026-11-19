using FamilyResilience.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class ConnectionDefaultsTests
{
    [Fact]
    public void Gss_encryption_is_off_unless_the_connection_string_asks_for_it()
    {
        // Npgsql otherwise probes for Kerberos (libgssapi) on every connection and logs a misleading error in containers.
        var builder = new NpgsqlConnectionStringBuilder(
            FamilyResilienceDbContextOptions.WithConnectionDefaults("Host=db;Database=azwah"));

        Assert.Equal(GssEncryptionMode.Disable, builder.GssEncryptionMode);
    }

    [Theory]
    [InlineData("Host=db;Database=azwah;GSS Encryption Mode=Require")]
    [InlineData("Host=db;Database=azwah;GssEncryptionMode=Require")]
    public void An_explicit_gss_setting_is_kept(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(FamilyResilienceDbContextOptions.WithConnectionDefaults(connectionString));

        Assert.Equal(GssEncryptionMode.Require, builder.GssEncryptionMode);
    }

    [Fact]
    public void The_database_context_uses_the_defaults()
    {
        var options = new DbContextOptionsBuilder<FamilyResilienceDbContext>();
        FamilyResilienceDbContextOptions.Configure(options, "Host=db;Database=azwah");
        using var context = new FamilyResilienceDbContext(options.Options);

        var builder = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());

        Assert.Equal(GssEncryptionMode.Disable, builder.GssEncryptionMode);
    }
}
