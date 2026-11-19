using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Security;

public sealed class ProductionHostTests
{
    private static List<string> Routes(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

    [Fact]
    public void Test_only_endpoints_are_not_part_of_the_real_api()
    {
        using var factory = new NoDatabaseApiFactory();

        var routes = Routes(factory);

        Assert.Contains(routes, route => route.StartsWith("api/v1/me", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route.StartsWith("test-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_check_does_see_test_only_endpoints_when_the_test_host_adds_them()
    {
        using var factory = new NoDatabaseApiFactory()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(TestServices.Configure));

        Assert.Contains(Routes(factory), route => route.StartsWith("test-only", StringComparison.OrdinalIgnoreCase));
    }
}
