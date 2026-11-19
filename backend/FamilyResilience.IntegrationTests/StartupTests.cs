using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests;

public sealed class StartupTests
{
    [Fact]
    public void Api_refuses_to_start_without_a_connection_string()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "" }));
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Default", Describe(exception), StringComparison.Ordinal);
    }

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";
}
