using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Security;

[Collection(ApiCollection.Name)]
public sealed class CorsTests(ApiFactory factory)
{
    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Preflight_from_an_allowed_origin_is_accepted()
    {
        using var response = await PreflightAsync(ApiFactory.AllowedOrigin);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(ApiFactory.AllowedOrigin, Assert.Single(values));
    }

    [Fact]
    public async Task Preflight_from_another_origin_gets_no_cors_grant()
    {
        using var response = await PreflightAsync("https://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
