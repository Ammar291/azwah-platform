using System.Net;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// Every participant route that names a household, member, assignment or session, tried by a participant of another
/// household with the victim's real IDs: each answers 404 as if the resource did not exist, nothing changes, and each
/// attempt is audited as a denied access.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class CrossHouseholdSweepTests(ParticipantApi api)
{
    private static readonly string[] Parameters = ["householdId", "memberId", "assignmentId", "sessionId"];

    private ApiFactory Factory => api.Factory;

    private async Task<string> SnapshotAsync(Enrolment owner, Guid sessionId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var parts = new List<string>();
        foreach (var path in new[]
                 {
                     $"/api/v1/households/{owner.Household.Id}",
                     $"/api/v1/households/{owner.Household.Id}/members",
                     $"/api/v1/sessions/{sessionId}",
                     $"/api/v1/sessions/{sessionId}/responses",
                     "/api/v1/me/consents",
                     "/api/v1/assignments",
                 })
        {
            using var response = await owner.Client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            parts.Add(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        return string.Join('\n', parts);
    }

    [Fact]
    public async Task Every_household_member_assignment_and_session_route_answers_404_to_another_household_and_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        var sessionId = await Journeys.AnsweredSessionAsync(owner, api, owner.Self, "A", 0, cancellationToken);
        var assignmentId = await Journeys.AssignmentAsync(owner, owner.Aged(68), "B", cancellationToken);
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var victim = new Dictionary<string, string>
        {
            ["householdId"] = owner.Household.Id.ToString(),
            ["memberId"] = owner.Self.ToString(),
            ["assignmentId"] = assignmentId.ToString(),
            ["sessionId"] = sessionId.ToString(),
        };
        var routes = RouteInventory.Of(Factory.Services)
            .Where(route => route.Policies.Count > 0 && route.Policies.All(RouteInventory.ParticipantPolicies.Contains))
            .Where(route => Parameters.Any(route.HasParameter))
            .ToList();
        var before = await SnapshotAsync(owner, sessionId);
        var wrong = new List<string>();

        foreach (var route in routes)
        {
            using var request = route.Request(victim);
            using var response = await stranger.Client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                wrong.Add($"{route} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        Assert.True(routes.Count >= 12, $"Only {routes.Count} routes were swept.");
        Assert.Equal(before, await SnapshotAsync(owner, sessionId));
        var trail = await Factory.QueryAsync(db => db.AuditEvents.AsNoTracking()
            .Where(audit => audit.ActorId == stranger.Subject && audit.ResourceType != "platform_role")
            .ToListAsync(cancellationToken));
        var denied = trail.Where(audit => audit.Action == "authorization.resource.denied").ToList();
        Assert.Equal(routes.Count, denied.Count);
        Assert.All(denied, audit => Assert.Equal(AuditOutcome.Denied, audit.Outcome));
        Assert.Subset(victim.Values.ToHashSet(), denied.Select(audit => audit.ResourceId!).ToHashSet());
        Assert.DoesNotContain(trail, audit => audit.Outcome == AuditOutcome.Succeeded && victim.Values.Contains(audit.ResourceId));
    }

    [Fact]
    public async Task Another_households_member_through_my_own_household_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var through = new Dictionary<string, string>
        {
            ["householdId"] = stranger.Household.Id.ToString(),
            ["memberId"] = owner.Aged(68).ToString(),
        };
        var routes = RouteInventory.Of(Factory.Services)
            .Where(route => route.HasParameter("householdId") && route.HasParameter("memberId"))
            .ToList();

        foreach (var route in routes)
        {
            using var request = route.Request(through);
            if (request.Content is not null)
            {
                // A valid change, so the refusal can only come from the member lookup.
                request.Content = new StringContent("""{"employmentStatus":"retired"}""", System.Text.Encoding.UTF8, "application/json");
            }

            using var response = await stranger.Client.SendAsync(request, cancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{route} answered {(int)response.StatusCode}");
            Assert.Equal("households.member_not_found", await response.ProblemCodeAsync(cancellationToken));
        }

        Assert.NotEmpty(routes);
    }

    [Fact]
    public async Task Consent_for_another_households_member_answers_404_and_records_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var policy = await stranger.Client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var response = await stranger.Client.PostJsonAsync(
            "/api/v1/consent", new { memberId = owner.Self, policyVersionId = policy.Id, decision = "grant" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("households.member_not_found", await response.ProblemCodeAsync(cancellationToken));
        Assert.False(await Factory.QueryAsync(db => db.ConsentRecords.AnyAsync(
            record => record.FamilyMemberId == FamilyMemberId.From(owner.Self), cancellationToken)));
    }
}
