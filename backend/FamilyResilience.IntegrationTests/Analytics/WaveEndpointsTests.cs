using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Analytics;

/// <summary>The wave list behind the dashboard's wave filters. Planned waves far in the future sort first.</summary>
[Collection(ApiCollection.Name)]
public sealed class WaveEndpointsTests(ApiFactory factory)
{
    private async Task<Wave> PlannedWaveAsync(int yearsAhead, CancellationToken cancellationToken)
    {
        var wave = TestData.Ok(Wave.Plan(
            TestData.UniqueCode("filter_wave"),
            TestData.Now.AddYears(yearsAhead),
            TestData.Now.AddYears(yearsAhead).AddDays(180),
            TestData.Now));
        await factory.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IWaveRepository>().Add(wave);
                return Task.CompletedTask;
            },
            cancellationToken);
        return wave;
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_who_filter_by_wave_get_waves_newest_first(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = await PlannedWaveAsync(50, cancellationToken);
        var later = await PlannedWaveAsync(51, cancellationToken);
        using var client = factory.As(ApiClients.NewSubject("staff"), role);

        using var response = await client.GetAsync("/api/v1/admin/waves?page=1&pageSize=100", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResponse<WaveResponse>>(cancellationToken);
        var codes = page.Items.Select(wave => wave.Code).ToList();
        Assert.True(codes.IndexOf(later.Code) < codes.IndexOf(earlier.Code));
        var listed = Assert.Single(page.Items, wave => wave.Id == later.Id.Value);
        Assert.Equal(
            new WaveResponse(later.Id.Value, later.Code, WaveStatus.Planned, later.OpensAt, later.ClosesAt),
            listed);
        Assert.True(page.TotalCount >= 2);
        Assert.Equal((1, 100), (page.Page, page.PageSize));
    }

    [Fact]
    public async Task A_page_size_over_100_returns_400_keyed_pageSize()
    {
        using var client = factory.AsResearcher();

        using var response = await client.GetAsync("/api/v1/admin/waves?pageSize=101", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pageSize", await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_anonymous_caller_gets_401()
    {
        using var client = factory.As(null);

        using var response = await client.GetAsync("/api/v1/admin/waves", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Participant)]
    [InlineData(Roles.SupportCoordinator)]
    public async Task Other_roles_get_403(string role)
    {
        using var client = factory.As(ApiClients.NewSubject("other"), role);

        using var response = await client.GetAsync("/api/v1/admin/waves", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
