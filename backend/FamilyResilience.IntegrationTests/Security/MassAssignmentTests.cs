using System.Net;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Analytics;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// Request bodies bind to request records that name only what a client may set; IDs, owners, statuses and accounts come
/// from the route, the token and the platform's records. Extra fields are ignored, so they can never set any of these.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class MassAssignmentTests(ParticipantApi api)
{
    private static readonly Guid Injected = Guid.Parse("0199f1a2-dead-7000-8000-00000000beef");

    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task Creating_a_household_ignores_ids_owners_and_statuses_in_the_body()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("participant");
        using var client = Factory.AsParticipant(subject);
        var head = new
        {
            id = Injected,
            householdId = Injected,
            hasAccount = true,
            birthYear = TestData.Today.Year - 42,
            birthMonth = TestData.Today.Month,
            employmentStatus = "employed",
            isHeadOfHousehold = true,
            isPrimaryIncomeEarner = true,
            isSelf = true,
        };

        using var response = await client.PostJsonAsync(
            "/api/v1/households",
            new { id = Injected, reference = "AZ-HACKED", status = "withdrawn", managerUserId = Injected, role = "manager", members = new[] { head } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var household = await response.ReadAsync<HouseholdResponse>(cancellationToken);
        Assert.NotEqual(Injected, household.Id);
        Assert.NotEqual("AZ-HACKED", household.Reference);
        Assert.Equal(HouseholdStatus.Active, household.Status);
        var members = await client.MembersAsync(household.Id, cancellationToken);
        Assert.DoesNotContain(members, member => member.Id == Injected);
        using var me = await client.GetAsync("/api/v1/me", cancellationToken);
        var mine = (await me.ReadAsync<MeResponse>(cancellationToken)).Household!;
        Assert.Equal((household.Id, HouseholdRole.Manager), (mine.HouseholdId, mine.Role));
    }

    [Fact]
    public async Task Updating_a_member_cannot_move_them_to_another_household_or_give_them_an_account()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var mine = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(68));
        using var other = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var parentId = mine.Aged(68);

        using var response = await mine.Client.PatchJsonAsync(
            $"/api/v1/households/{mine.Household.Id}/members/{parentId}",
            new { householdId = other.Household.Id, hasAccount = true, isCaller = true, id = Injected, employmentStatus = "unemployed" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<MemberResponse>(cancellationToken);
        Assert.Equal((parentId, false, false, EmploymentStatus.Unemployed), (updated.Id, updated.HasAccount, updated.IsCaller, updated.EmploymentStatus));
        Assert.Equal(mine.Household.Id, await Factory.QueryAsync(db => db.Households.AsNoTracking()
            .Where(household => household.Members.Any(member => member.Id == FamilyMemberId.From(parentId)))
            .Select(household => household.Id.Value)
            .SingleAsync(cancellationToken)));
    }

    [Fact]
    public async Task An_export_request_cannot_choose_its_requester()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = ApiClients.NewSubject("researcher");
        using var client = Factory.AsResearcher(subject);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/exports")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new { dataset = "score_results", columns = new[] { "instrument_code" }, requestedBy = "another-researcher", status = "completed" },
                options: ApiJson.Options),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var export = await response.ReadAsync<ExportResponse>(cancellationToken);
        var job = await Factory.QueryAsync(db => db.ExportJobs.AsNoTracking().SingleAsync(job => job.Id == ExportJobId.From(export.Id), cancellationToken));
        Assert.Equal((subject, ExportStatus.Queued), (job.RequestedBy, job.Status));
    }

    [Fact]
    public async Task Recording_consent_cannot_name_who_granted_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        var policyId = api.Policies[ConsentType.Participation].Id.Value;

        using var response = await enrolment.Client.PostJsonAsync(
            "/api/v1/consent",
            new { memberId = enrolment.Self, policyVersionId = policyId, decision = "grant", grantedBy = "someone-else", grantedAt = "2020-01-01T00:00:00Z" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await Factory.QueryAsync(db => db.ConsentRecords.AsNoTracking()
            .SingleAsync(record => record.FamilyMemberId == FamilyMemberId.From(enrolment.Self), cancellationToken));
        Assert.Equal((enrolment.Subject, TestData.Now), (record.GrantedBy, record.GrantedAt));
    }
}
