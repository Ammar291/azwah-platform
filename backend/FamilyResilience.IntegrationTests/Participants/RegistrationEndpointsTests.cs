using System.Net;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// A new participant confirms the details their identity provider shows them once; the platform records only that a
/// platform user exists for the token subject, and when. Returning participants see the time and skip the step (D-97).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RegistrationEndpointsTests(ApiFactory factory)
{
    private const string Url = "/api/v1/me/registration";

    private static async Task<DateTimeOffset?> RegisteredAtAsync(HttpClient client)
    {
        using var me = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (await me.ReadAsync<MeResponse>(TestContext.Current.CancellationToken)).RegisteredAt;
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string body = """{"detailsConfirmed":true}""") =>
        client.PostRawJsonAsync(Url, body, TestContext.Current.CancellationToken);

    private Task<List<AuditEvent>> RegistrationEventsOfAsync(string subject) =>
        factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action == RegisterCaller.Registered)
            .ToListAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_new_participant_is_registered_once_201_then_200_with_the_same_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("registering");
        using var client = factory.AsParticipant(subject);
        Assert.Null(await RegisteredAtAsync(client));

        using var first = await RegisterAsync(client);
        using var second = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("/api/v1/me", first.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var created = await first.ReadAsync<RegistrationResponse>(cancellationToken);
        var repeated = await second.ReadAsync<RegistrationResponse>(cancellationToken);
        Assert.Equal(created.RegisteredAt, repeated.RegisteredAt);
        Assert.Equal(TimeSpan.Zero, created.RegisteredAt.Offset);
        Assert.Equal(created.RegisteredAt, await RegisteredAtAsync(client));
        var recorded = Assert.Single(await RegistrationEventsOfAsync(subject));
        Assert.Equal(("platform_user", AuditOutcome.Succeeded, AuditActorType.User), (recorded.ResourceType, recorded.Outcome, recorded.ActorType));
    }

    [Fact]
    public async Task The_registration_time_is_written_as_an_iso_utc_timestamp()
    {
        using var client = factory.AsParticipant();

        using var response = await RegisterAsync(client);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Matches("""^\{"registeredAt":"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|\+00:00)"\}$""", body);
    }

    [Fact]
    public async Task A_registered_participant_can_still_create_their_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsParticipant();
        using var registered = await RegisterAsync(client);
        var registeredAt = (await registered.ReadAsync<RegistrationResponse>(cancellationToken)).RegisteredAt;

        var household = await client.CreateHouseholdAsync(cancellationToken);

        Assert.NotEqual(Guid.Empty, household.Id);
        Assert.Equal(registeredAt, await RegisteredAtAsync(client));
    }

    [Fact]
    public async Task A_participant_registered_by_creating_a_household_is_already_registered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.AsParticipant();
        await client.CreateHouseholdAsync(cancellationToken);
        var registeredAt = await RegisteredAtAsync(client);
        Assert.NotNull(registeredAt);

        using var response = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(registeredAt, (await response.ReadAsync<RegistrationResponse>(cancellationToken)).RegisteredAt);
    }

    [Theory]
    [InlineData("""{"detailsConfirmed":false}""")]
    [InlineData("""{"detailsConfirmed":null}""")]
    [InlineData("""{}""")]
    public async Task Without_an_explicit_confirmation_it_returns_400_keyed_by_details_confirmed_and_registers_nobody(string body)
    {
        var subject = ApiClients.NewSubject("unconfirmed");
        using var client = factory.AsParticipant(subject);

        using var response = await RegisterAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("detailsConfirmed", await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
        Assert.Null(await RegisteredAtAsync(client));
        Assert.Empty(await RegistrationEventsOfAsync(subject));
    }

    [Fact]
    public async Task An_anonymous_caller_gets_401()
    {
        using var client = factory.As(null);

        using var response = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.SystemAdministrator)]
    [InlineData(Roles.SupportCoordinator)]
    public async Task Staff_get_403_and_are_not_registered(string role)
    {
        var subject = ApiClients.NewSubject("staff");
        using var client = factory.As(subject, role);

        using var response = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await RegisteredAtAsync(client));
    }
}
