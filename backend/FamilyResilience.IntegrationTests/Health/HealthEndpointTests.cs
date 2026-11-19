using System.Net;
using System.Net.Http.Json;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Health;

[Collection(ApiCollection.Name)]
public sealed class HealthEndpointTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Reports_healthy_when_the_database_is_reachable(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(TestContext.Current.CancellationToken);
        Assert.Equal("Healthy", body!.Status);
    }

    private sealed record HealthBody(string Status);
}
