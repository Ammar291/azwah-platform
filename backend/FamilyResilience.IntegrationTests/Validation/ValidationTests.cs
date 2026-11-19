using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Validation;

[Collection(ApiCollection.Name)]
public sealed class ValidationTests(ApiFactory factory)
{
    [Fact]
    public async Task Invalid_body_returns_400_with_camel_case_error_keys()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/test-only/echo", new { name = "", age = 150 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var errors = json.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("age", out _));
    }

    [Fact]
    public async Task Malformed_json_returns_400_without_parser_text_and_keyed_by_json_path()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("""{"name":"Layla","age":}""", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/test-only/echo", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var errors = json.RootElement.GetProperty("errors").EnumerateObject().ToList();
        Assert.Contains(errors, error => error.Name.StartsWith('$'));
        foreach (var forbidden in new[] { "Path:", "LineNumber", "FamilyResilience", "deserialization" })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Valid_body_reaches_the_action()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/test-only/echo", new { name = "Layla", age = 34 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echoed = await response.Content.ReadFromJsonAsync<EchoRequest>(TestContext.Current.CancellationToken);
        Assert.Equal("Layla", echoed!.Name);
    }
}
