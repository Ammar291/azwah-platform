using System.Net;
using FamilyResilience.Api.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Roles are granted in the identity provider; the platform audits each role it sees a subject gain or lose, once.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RoleObservationTests(ApiFactory factory)
{
    private async Task CallAsync(string subject, params string[] roles)
    {
        using var client = factory.As(subject, roles);
        using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<List<(string Action, string? Role)>> RoleEventsAsync(string subject) =>
        factory.QueryAsync(async db => (await db.AuditEvents.AsNoTracking()
                .Where(audit => audit.ActorId == subject && audit.ResourceType == "platform_role")
                .OrderBy(audit => audit.OccurredAt).ThenBy(audit => audit.Action).ThenBy(audit => audit.ResourceId)
                .ToListAsync(TestContext.Current.CancellationToken))
            .Select(audit => (audit.Action, audit.ResourceId))
            .ToList());

    private void ForgetObservations() => factory.Services.GetRequiredService<RoleObservationCache>().Clear();

    [Fact]
    public async Task The_first_request_of_a_subject_records_its_roles()
    {
        var subject = ApiClients.NewSubject("roles");

        await CallAsync(subject, "researcher", "program_administrator");

        Assert.Equal(
            [("identity.role.added", "program_administrator"), ("identity.role.added", "researcher")],
            await RoleEventsAsync(subject));
        var stored = await factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .FirstAsync(audit => audit.ActorId == subject && audit.ResourceType == "platform_role", TestContext.Current.CancellationToken));
        Assert.Equal((AuditActorType.User, AuditOutcome.Succeeded), (stored.ActorType, stored.Outcome));
        Assert.Equal(["program_administrator", "researcher"], await factory.QueryAsync(db => db.RoleSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Subject == subject).Select(snapshot => snapshot.Roles).SingleAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_new_role_in_the_token_is_recorded_as_added_and_a_missing_one_as_removed()
    {
        var subject = ApiClients.NewSubject("roles");
        await CallAsync(subject, "researcher");

        await CallAsync(subject, "system_administrator");

        Assert.Equal(
            [("identity.role.added", "researcher"), ("identity.role.added", "system_administrator"), ("identity.role.removed", "researcher")],
            (await RoleEventsAsync(subject)).Order().ToList());
    }

    [Fact]
    public async Task Repeating_the_same_roles_writes_nothing_even_after_the_cache_is_cleared()
    {
        var subject = ApiClients.NewSubject("roles");
        await CallAsync(subject, "participant");

        await CallAsync(subject, "participant");
        ForgetObservations();
        await CallAsync(subject, "participant");
        await CallAsync(subject, "participant");

        Assert.Equal([("identity.role.added", "participant")], await RoleEventsAsync(subject));
    }

    [Fact]
    public async Task Unknown_role_strings_are_not_recorded()
    {
        var subject = ApiClients.NewSubject("roles");

        await CallAsync(subject, "participant", "root");

        Assert.Equal([("identity.role.added", "participant")], await RoleEventsAsync(subject));
    }

    [Fact]
    public async Task Concurrent_first_requests_record_the_roles_once()
    {
        var subject = ApiClients.NewSubject("roles");

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CallAsync(subject, "researcher")));

        Assert.Equal([("identity.role.added", "researcher")], await RoleEventsAsync(subject));
    }

    [Fact]
    public async Task An_anonymous_request_records_nothing()
    {
        var before = await factory.QueryAsync(db => db.RoleSnapshots.CountAsync(TestContext.Current.CancellationToken));
        using var client = factory.As(null);

        using var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, await factory.QueryAsync(db => db.RoleSnapshots.CountAsync(TestContext.Current.CancellationToken)));
    }
}
