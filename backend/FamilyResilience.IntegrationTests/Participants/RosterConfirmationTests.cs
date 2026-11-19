using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class RosterConfirmationTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private static async Task<RosterConfirmationResponse> ConfirmAsync(HttpClient client, Guid householdId, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync($"/api/v1/households/{householdId}/confirm-roster", new { }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<RosterConfirmationResponse>(cancellationToken);
    }

    [Fact]
    public async Task Confirming_assigns_each_eligible_member_from_the_approved_rules_and_reveals_no_reasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(
            cancellationToken, Rosters.Head(), Rosters.Spouse(), Rosters.Parent(68), Rosters.Child(9));
        var members = await client.MembersAsync(household.Id, cancellationToken);
        var head = members.Single(member => member.IsHeadOfHousehold);
        var parent = members.Single(member => member.RelationshipToHousehold == HouseholdRelationship.Parent);

        using var response = await client.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("reason", body, StringComparison.OrdinalIgnoreCase);
        var confirmation = await response.ReadAsync<RosterConfirmationResponse>(cancellationToken);
        Assert.Equal((api.Wave.Id.Value, api.Wave.Code, TestData.Now), (confirmation.WaveId, confirmation.WaveCode, confirmation.RosterConfirmedAt));
        Assert.Equal(
            [(head.Id, "A"), (parent.Id, "B")],
            confirmation.Assignments.Select(assignment => (assignment.MemberId, assignment.SectionCode!)).OrderBy(pair => pair.Item2));
        Assert.All(confirmation.Assignments, assignment =>
        {
            Assert.Equal(api.Instrument.Code, assignment.InstrumentCode);
            Assert.Equal(AssignmentStatus.Assigned, assignment.Status);
        });
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "households.roster.confirmed" && audit.ResourceId == household.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task Confirming_again_keeps_the_assignments_and_creates_nothing_new()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Parent(70));
        var first = await ConfirmAsync(client, household.Id, cancellationToken);

        var second = await ConfirmAsync(client, household.Id, cancellationToken);

        Assert.Equal(
            first.Assignments.Select(assignment => assignment.AssignmentId).Order(),
            second.Assignments.Select(assignment => assignment.AssignmentId).Order());
        var memberIds = (await client.MembersAsync(household.Id, cancellationToken)).Select(member => FamilyMemberId.From(member.Id)).ToList();
        Assert.Equal(2, await Factory.QueryAsync(db => db.AssessmentAssignments.CountAsync(
            assignment => memberIds.Contains(assignment.FamilyMemberId), cancellationToken)));
    }

    [Fact]
    public async Task A_member_who_no_longer_qualifies_loses_a_not_started_assignment_on_reconfirmation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Parent(70));
        var first = await ConfirmAsync(client, household.Id, cancellationToken);
        var parentAssignment = first.Assignments.Single(assignment => assignment.SectionCode == "B");

        using var removed = await client.PatchJsonAsync(
            $"/api/v1/households/{household.Id}/members/{parentAssignment.MemberId}", new { status = "inactive" }, cancellationToken);
        var second = await ConfirmAsync(client, household.Id, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain(second.Assignments, assignment => assignment.AssignmentId == parentAssignment.AssignmentId);
        var saved = await Factory.QueryAsync(db => db.AssessmentAssignments.SingleAsync(
            assignment => assignment.Id == AssessmentAssignmentId.From(parentAssignment.AssignmentId), cancellationToken));
        Assert.Equal(AssignmentStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task A_roster_change_clears_the_confirmation_until_it_is_confirmed_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);
        await ConfirmAsync(client, household.Id, cancellationToken);

        using var added = await client.PostJsonAsync($"/api/v1/households/{household.Id}/members", Rosters.Child(2), cancellationToken);
        using var read = await client.GetAsync($"/api/v1/households/{household.Id}", cancellationToken);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Null((await read.ReadAsync<HouseholdResponse>(cancellationToken)).RosterConfirmedAt);
    }

    public static TheoryData<object[], string> IncompleteRosters => new()
    {
        { [Rosters.Member(40, "spouse", "employed", head: false, earner: true, self: true)], "households.roster_has_no_head" },
        { [Rosters.Head(), Rosters.Member(39, null, "employed", head: false, earner: false, self: false)], "households.roster_missing_relationship" },
        { [Rosters.Head(), Rosters.Member(39, "spouse", null, head: false, earner: false, self: false)], "households.roster_missing_employment_status" },
    };

    [Theory]
    [MemberData(nameof(IncompleteRosters))]
    public async Task An_incomplete_roster_returns_409_and_assigns_nothing(object[] roster, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, roster);

        using var response = await client.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await response.ProblemCodeAsync(cancellationToken));
        var memberIds = (await client.MembersAsync(household.Id, cancellationToken)).Select(member => FamilyMemberId.From(member.Id)).ToList();
        Assert.Equal(0, await Factory.QueryAsync(db => db.AssessmentAssignments.CountAsync(
            assignment => memberIds.Contains(assignment.FamilyMemberId), cancellationToken)));
    }

    [Fact]
    public async Task A_withdrawn_household_cannot_confirm_its_roster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);
        using var withdrawn = await client.PatchJsonAsync($"/api/v1/households/{household.Id}", new { status = "withdrawn" }, cancellationToken);

        using var response = await client.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.not_active", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Another_familys_roster_cannot_be_confirmed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.AsParticipant();
        var household = await owner.CreateHouseholdAsync(cancellationToken);
        using var stranger = Factory.AsParticipant();
        using var staff = Factory.As(ApiClients.NewSubject("staff"), Roles.SystemAdministrator);

        using var asStranger = await stranger.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);
        using var asStaff = await staff.PostJsonAsync($"/api/v1/households/{household.Id}/confirm-roster", new { }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, asStranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        using var read = await owner.GetAsync($"/api/v1/households/{household.Id}", cancellationToken);
        Assert.Null((await read.ReadAsync<HouseholdResponse>(cancellationToken)).RosterConfirmedAt);
    }
}
