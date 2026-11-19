using System.Net;
using System.Net.Http.Json;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Health;

public sealed class HealthWithoutDatabaseTests
{
    [Fact]
    public async Task Live_is_healthy_without_a_database()
    {
        await using var factory = new NoDatabaseApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_returns_503_unhealthy_without_revealing_why()
    {
        await using var factory = new NoDatabaseApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("database", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", raw, StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestContext.Current.CancellationToken);
        Assert.Equal("Unhealthy", body!.Status);
    }

    private sealed record HealthBody(string Status);
}
