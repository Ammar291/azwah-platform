using System.Net;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace FamilyResilience.IntegrationTests.OpenApi;

[Collection(ApiCollection.Name)]
public sealed class OpenApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Document_is_served_in_development()
    {
        await using var development = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("/api/v1/me", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Document_is_not_served_outside_development()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
