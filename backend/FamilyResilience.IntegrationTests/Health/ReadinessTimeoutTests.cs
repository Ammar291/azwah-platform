using System.Diagnostics;
using System.Net;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Health;

public sealed class ReadinessTimeoutTests
{
    [Fact]
    public async Task Ready_answers_503_quickly_when_the_database_stops_responding()
    {
        await using var silentDatabase = new SilentTcpServer();
        await using var factory = new UnresponsiveDatabaseApiFactory(silentDatabase.Port);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(6),
            $"Readiness took {stopwatch.Elapsed.TotalSeconds:F1} s; it must give up after its 3 s deadline, well before the 15 s connect timeout.");
    }
}
