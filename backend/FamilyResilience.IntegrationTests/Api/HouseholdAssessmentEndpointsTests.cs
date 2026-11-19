using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Assignments;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Api;

[Collection(EngineApiCollection.Name)]
public sealed class HouseholdAssessmentEndpointsTests(ApiFactory factory)
{
    private const string HeadOrEarner = """{"any":[{"isHeadOfHousehold":true},{"isPrimaryIncomeEarner":true}]}""";

    [Fact]
    public async Task Assigning_returns_200_with_the_created_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await factory.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await factory.OpenWaveAsync(cancellationToken);
        var (instrument, _, _) = await factory.EligibilityScenarioAsync(cancellationToken, ("A", HeadOrEarner));
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/households/{household.Id.Value}/assign", new { waveId = wave.Id.Value }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.ReadAsync<AssignmentRunResponse>(cancellationToken);
        Assert.Single(run.Created, summary => summary.InstrumentCode == instrument.Code);
    }

    [Fact]
    public async Task Evaluating_returns_200_with_decisions_and_the_plan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await factory.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await factory.OpenWaveAsync(cancellationToken);
        var (instrument, _, _) = await factory.EligibilityScenarioAsync(cancellationToken, ("A", HeadOrEarner));
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/households/{household.Id.Value}/evaluate-eligibility", new { waveId = wave.Id.Value }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evaluation = await response.ReadAsync<EligibilityEvaluationResponse>(cancellationToken);
        Assert.Contains(evaluation.Decisions, decision => decision.InstrumentCode == instrument.Code && decision.Eligible == true);
        Assert.Contains("\"outcome\":\"eligible\"", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_wave_id_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync($"/api/v1/households/{Guid.NewGuid()}/assign", new { waveId = Guid.Empty }, cancellationToken);

        await AssertWaveIdRejectedAsync(response, cancellationToken);
    }

    [Fact]
    public async Task An_unknown_household_or_wave_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await factory.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await factory.OpenWaveAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var noHousehold = await client.PostJsonAsync($"/api/v1/households/{Guid.NewGuid()}/assign", new { waveId = wave.Id.Value }, cancellationToken);
        using var noWave = await client.PostJsonAsync($"/api/v1/households/{household.Id.Value}/assign", new { waveId = Guid.NewGuid() }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, noHousehold.StatusCode);
        Assert.Equal("households.household_not_found", await noHousehold.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, noWave.StatusCode);
        Assert.Equal("assignments.wave_not_found", await noWave.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Evaluating_with_an_empty_wave_id_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/households/{Guid.NewGuid()}/evaluate-eligibility", new { waveId = Guid.Empty }, cancellationToken);

        await AssertWaveIdRejectedAsync(response, cancellationToken);
    }

    [Fact]
    public async Task Evaluating_for_an_unknown_household_or_wave_returns_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await factory.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await factory.OpenWaveAsync(cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var noHousehold = await client.PostJsonAsync(
            $"/api/v1/households/{Guid.NewGuid()}/evaluate-eligibility", new { waveId = wave.Id.Value }, cancellationToken);
        using var noWave = await client.PostJsonAsync(
            $"/api/v1/households/{household.Id.Value}/evaluate-eligibility", new { waveId = Guid.NewGuid() }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, noHousehold.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noWave.StatusCode);
        Assert.Equal("households.household_not_found", await noHousehold.ProblemCodeAsync(cancellationToken));
        Assert.Equal("assignments.wave_not_found", await noWave.ProblemCodeAsync(cancellationToken));
    }

    [Theory]
    [InlineData("assign")]
    [InlineData("evaluate-eligibility")]
    public async Task A_closed_wave_returns_409(string action)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await factory.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await factory.OpenWaveAsync(cancellationToken);
        await factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IWaveRepository>().GetAsync(wave.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The wave was not saved.");
                TestData.Ok(tracked.Close(TestData.Now));
            },
            cancellationToken);
        using var client = factory.AsSystemAdministrator();

        using var response = await client.PostJsonAsync(
            $"/api/v1/households/{household.Id.Value}/{action}", new { waveId = wave.Id.Value }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("assignments.wave_closed", await response.ProblemCodeAsync(cancellationToken));
    }

    private static async Task AssertWaveIdRejectedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("waveId", out _));
    }
}
