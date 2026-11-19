using System.Net;
using System.Net.Http.Json;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>The apps record each sign-in and sign-out, so the platform's audit trail shows when a staff or family account was used.</summary>
[Collection(ApiCollection.Name)]
public sealed class SessionEventEndpointsTests(ApiFactory factory)
{
    private Task<List<AuditEvent>> EventsOfAsync(string subject) =>
        factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == subject && audit.Action.StartsWith("identity.session."))
            .ToListAsync(TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("mobile", Roles.Participant)]
    [InlineData("admin", Roles.Researcher)]
    [InlineData("admin", Roles.SystemAdministrator)]
    public async Task A_sign_in_record_answers_204_and_audits_the_client(string clientCode, string role)
    {
        var subject = ApiClients.NewSubject("signin");
        using var client = factory.As(subject, role);

        using var response = await client.PostAsJsonAsync("/api/v1/me/sign-ins", new { client = clientCode }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var recorded = Assert.Single(await EventsOfAsync(subject));
        Assert.Equal(
            ("identity.session.started", "auth_client", clientCode, AuditOutcome.Succeeded, AuditActorType.User),
            (recorded.Action, recorded.ResourceType, recorded.ResourceId, recorded.Outcome, recorded.ActorType));
    }

    [Fact]
    public async Task A_sign_out_record_audits_the_end()
    {
        var subject = ApiClients.NewSubject("signout");
        using var client = factory.AsParticipant(subject);

        using var response = await client.PostAsJsonAsync("/api/v1/me/sign-outs", new { client = "mobile" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var recorded = Assert.Single(await EventsOfAsync(subject));
        Assert.Equal(("identity.session.ended", "mobile"), (recorded.Action, recorded.ResourceId));
    }

    [Theory]
    [InlineData("""{"client":"browser-extension"}""")]
    [InlineData("""{"client":""}""")]
    [InlineData("""{}""")]
    public async Task An_unknown_or_missing_client_returns_400_keyed_by_client_and_records_nothing(string body)
    {
        var subject = ApiClients.NewSubject("signin");
        using var client = factory.AsParticipant(subject);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/me/sign-ins", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("client", await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await EventsOfAsync(subject));
    }

    [Theory]
    [InlineData("/api/v1/me/sign-ins")]
    [InlineData("/api/v1/me/sign-outs")]
    public async Task An_anonymous_caller_gets_401(string path)
    {
        using var client = factory.As(null);

        using var response = await client.PostAsJsonAsync(path, new { client = "mobile" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
