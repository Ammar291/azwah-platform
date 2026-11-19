using System.Net;
using System.Text.Json;
using FamilyResilience.Api.Errors;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.Errors;

[Collection(ApiCollection.Name)]
public sealed class ErrorHandlingTests(ApiFactory factory)
{
    [Fact]
    public async Task Unhandled_exception_returns_a_generic_problem_without_internal_detail()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test-only/throw", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(TestOnlyController.SensitiveDetail, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", raw, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    public async Task A_bad_request_exception_returns_its_status_as_a_generic_problem_logged_as_a_warning(int status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logs = new CapturingLoggerProvider();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();
        var startup = logs.Entries.Count;

        using var response = await client.GetAsync($"/test-only/bad-request/{status}", cancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(TestOnlyController.SensitiveDetail, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("BadHttpRequestException", raw, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
        var request = logs.Entries.Skip(startup).ToList();
        var logged = Assert.Single(request, entry => entry.Category == typeof(GlobalExceptionHandler).FullName);
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.DoesNotContain(request, entry => entry.Level >= LogLevel.Error);
    }
}
