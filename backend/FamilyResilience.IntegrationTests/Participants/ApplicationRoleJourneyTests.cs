using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// The API connected as the deployed application role (<c>azwah_app</c>, granted by application-role.sql) can still do
/// everything a family does: enrol, consent, confirm the roster, answer and submit (scoring, routing, idempotency
/// keys, row locks), with its audit events, role observations and outbox messages; and the erasure the retention job
/// runs inside the API (D-88) works with its grants.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class ApplicationRoleJourneyTests(ParticipantApi api)
{
    private async Task<WebApplicationFactory<Program>> AsApplicationRoleAsync(string? erasure = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var superuser = api.Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        // The test database's tables are owned by the superuser that migrated them, so it plays the owner here.
        await DatabaseRoles.SetUpAsync(superuser, superuser, cancellationToken);
        return api.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = DatabaseRoles.As(superuser, DatabaseRoles.App),
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Erasure:ConsentRecords"] = erasure,
            })));
    }

    [Fact]
    public async Task A_whole_participant_journey_works_as_the_application_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await AsApplicationRoleAsync();
        var subject = ApiClients.NewSubject("app-role");
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "participant");

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Parent(68));
        using var enrolment = new Enrolment(client, subject, household, await client.MembersAsync(household.Id, cancellationToken));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(client, sessionId, Guid.NewGuid(), cancellationToken);
        using var signIn = await client.PostJsonAsync("/api/v1/me/sign-ins", new { client = "mobile" }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        Assert.True(host.Services.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing"));
    }

    [Fact]
    public async Task A_scored_household_can_be_erased_as_the_application_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await AsApplicationRoleAsync(erasure: "delete");
        var subject = ApiClients.NewSubject("app-role-erasure");
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, "participant");
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Parent(68));
        using var enrolment = new Enrolment(client, subject, household, await client.MembersAsync(household.Id, cancellationToken));
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(client, sessionId, Guid.NewGuid(), cancellationToken);
        using var requested = await client.PostJsonAsync("/api/v1/me/privacy-requests", new { type = "erasure" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);

        await using var scope = host.Services.CreateAsyncScope();
        var erased = await scope.ServiceProvider.GetRequiredService<EraseHousehold>()
            .HandleAsync(household.Id, household.Reference, withdrawFirst: true, cancellationToken);

        Assert.True(erased.IsSuccess, erased.Error?.Code);
        Assert.Equal(1, erased.Value["households"]);
        Assert.True(erased.Value["assessment_responses"] > 0);
    }
}
