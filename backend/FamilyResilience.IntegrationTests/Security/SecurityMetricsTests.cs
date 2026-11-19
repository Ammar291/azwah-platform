using System.Net;
using FamilyResilience.Api.Observability;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// The security counters the alert rules in <c>infra/monitoring/alert-rules.yml</c> watch. Each test uses its own host,
/// so it counts only its own requests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityMetricsTests(ApiFactory factory)
{
    private static HttpClient As(WebApplicationFactory<Program> host, string? subject, params string[] roles)
    {
        var client = host.CreateClient();
        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    [Fact]
    public async Task Refused_resources_and_roles_are_counted_by_kind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var measurements = new SecurityMeasurements(host);
        using var participant = As(host, ApiClients.NewSubject("metrics"), Roles.Participant);

        using var otherHousehold = await participant.GetAsync($"/api/v1/households/{Guid.NewGuid()}", cancellationToken);
        using var staffRoute = await participant.GetAsync("/api/v1/admin/referrals", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, otherHousehold.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffRoute.StatusCode);
        Assert.Equal(1, measurements.Total("azwah.authorization.denied", ("kind", "resource")));
        Assert.Equal(1, measurements.Total("azwah.authorization.denied", ("kind", "role")));
    }

    [Fact]
    public async Task Anonymous_refusals_are_not_counted_as_authorization_denials()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var measurements = new SecurityMeasurements(host);
        using var anonymous = As(host, null);

        using var response = await anonymous.GetAsync("/api/v1/admin/referrals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, measurements.Total("azwah.authorization.denied"));
    }

    [Fact]
    public async Task Role_changes_are_counted_by_change_and_role_with_first_sightings_apart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var measurements = new SecurityMeasurements(host);
        var subject = ApiClients.NewSubject("metrics-roles");

        foreach (var roles in new[] { new[] { Roles.Researcher }, [Roles.Researcher, Roles.SystemAdministrator], [Roles.SystemAdministrator] })
        {
            using var client = As(host, subject, roles);
            using var response = await client.GetAsync("/api/v1/me", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, measurements.Total("azwah.identity.role_changes", ("change", "added"), ("role", Roles.Researcher), ("first_seen", "true")));
        Assert.Equal(1, measurements.Total("azwah.identity.role_changes", ("change", "added"), ("role", Roles.SystemAdministrator), ("first_seen", "false")));
        Assert.Equal(1, measurements.Total("azwah.identity.role_changes", ("change", "removed"), ("role", Roles.Researcher), ("first_seen", "false")));
        Assert.Equal(3, measurements.Total("azwah.identity.role_changes"));
    }

    [Fact]
    public async Task A_role_change_seen_again_is_not_counted_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var measurements = new SecurityMeasurements(host);
        var subject = ApiClients.NewSubject("metrics-repeat");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            host.Services.GetRequiredService<FamilyResilience.Api.Identity.RoleObservationCache>().Clear();
            using var client = As(host, subject, Roles.Researcher);
            using var response = await client.GetAsync("/api/v1/me", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, measurements.Total("azwah.identity.role_changes"));
    }

    [Fact]
    public async Task A_retention_run_counts_erased_households_and_failed_erasures()
    {
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var measurements = new SecurityMeasurements(host);
        var metrics = host.Services.GetRequiredService<SecurityMetrics>();

        metrics.RetentionRan(RetentionReport.Nothing);
        metrics.RetentionRan(new RetentionReport(2, 5, 1, false, ["persistence.conflict"]));

        Assert.Equal(2, measurements.Total("azwah.retention.households_erased"));
        Assert.Equal(1, measurements.Total("azwah.retention.erasure_failures"));
    }
}
