using System.Net;
using System.Text.Json;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Households;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Rights requests (D-89): a family downloads a copy of its data and asks for access, correction or erasure; program
/// administrators list and close the requests. Each person reaches only the members they may act for (D-49).
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class PrivacyEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, object body) =>
        client.PostJsonAsync("/api/v1/me/privacy-requests", body, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private Task<int> AuditCountAsync(string action, string? actor = null, string? resourceId = null) =>
        Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && (actor == null || audit.ActorId == actor) && (resourceId == null || audit.ResourceId == resourceId),
            TestContext.Current.CancellationToken));

    [Fact]
    public async Task A_participant_asks_for_erasure_of_their_own_member_by_default_and_it_is_audited()
    {
        using var family = await Enrolment.CreateAsync(Factory, TestContext.Current.CancellationToken, Rosters.Head());

        using var response = await RequestAsync(family.Client, new { type = "erasure" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(family.Self, body.GetProperty("memberId").GetGuid());
        Assert.Equal(("erasure", "open"), (body.GetProperty("type").GetString(), body.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("closedAt").ValueKind);
        Assert.Equal(1, await AuditCountAsync("privacy.request.created", family.Subject, body.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task A_manager_asks_for_a_member_without_an_account_but_not_for_one_with_their_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        using var unlinked = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Parent(70));

        using var forRelative = await RequestAsync(unlinked.Client, new { type = "access", memberId = unlinked.Aged(70) });
        using var forAccountHolder = await RequestAsync(linked.Manager.Client, new { type = "access", memberId = linked.ParentId });
        using var forStranger = await RequestAsync(unlinked.Client, new { type = "access", memberId = linked.Manager.Self });

        Assert.Equal(HttpStatusCode.Created, forRelative.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forAccountHolder.StatusCode);
        Assert.Equal("households.member_not_found", await forAccountHolder.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, forStranger.StatusCode);
        Assert.Equal("households.member_not_found", await forStranger.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task One_open_request_per_member_and_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());

        using var first = await RequestAsync(family.Client, new { type = "correction" });
        using var again = await RequestAsync(family.Client, new { type = "correction" });
        using var other = await RequestAsync(family.Client, new { type = "access" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("privacy.request_already_open", await again.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Theory]
    [InlineData("""{"type":"forget_me"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"type":"erasure","memberId":"not-a-guid"}""")]
    public async Task An_invalid_request_returns_400(string body)
    {
        using var family = await Enrolment.CreateAsync(Factory, TestContext.Current.CancellationToken, Rosters.Head());

        using var response = await family.Client.PostRawJsonAsync("/api/v1/me/privacy-requests", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_member_with_their_own_account_lists_only_their_own_requests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        using var managers = await RequestAsync(linked.Manager.Client, new { type = "access" });
        using var parents = await RequestAsync(linked.Parent, new { type = "erasure" });

        using var mine = await linked.Parent.GetAsync("/api/v1/me/privacy-requests", cancellationToken);

        var items = (await JsonAsync(mine)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([linked.ParentId], items.Select(item => item.GetProperty("memberId").GetGuid()));
    }

    [Fact]
    public async Task The_data_export_holds_what_the_caller_may_see_and_nothing_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        var family = linked.Manager;
        var sessionId = await Journeys.AnsweredSessionAsync(family, api, family.Self, "A", 0, cancellationToken);
        using var request = await RequestAsync(family.Client, new { type = "access" });

        using var response = await family.Client.GetAsync("/api/v1/me/data-export", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var export = await JsonAsync(response);
        Assert.Equal(family.Subject, export.GetProperty("subject").GetString());
        Assert.Equal(family.Household.Id, export.GetProperty("household").GetProperty("householdId").GetGuid());
        var memberIds = export.GetProperty("members").EnumerateArray().Select(member => member.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(family.Self, memberIds);
        Assert.DoesNotContain(linked.ParentId, memberIds);
        Assert.All(export.GetProperty("assignments").EnumerateArray(), item => Assert.NotEqual(linked.ParentId, item.GetProperty("memberId").GetGuid()));
        var session = Assert.Single(export.GetProperty("sessions").EnumerateArray(), item => item.GetProperty("session").GetProperty("id").GetGuid() == sessionId);
        Assert.True(session.GetProperty("answers").GetArrayLength() > 0);
        Assert.Contains(export.GetProperty("consents").EnumerateArray(), consent => consent.GetProperty("consentType").GetString() == "participation");
        Assert.Single(export.GetProperty("privacyRequests").EnumerateArray());
        Assert.Equal(["score_results", "support_recommendations"], export.GetProperty("notIncluded").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain("totalScore", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, await AuditCountAsync("privacy.data_export.read", family.Subject));
    }

    [Fact]
    public async Task A_participant_without_a_household_gets_an_empty_export()
    {
        using var client = Factory.AsParticipant();

        using var response = await client.GetAsync("/api/v1/me/data-export", TestContext.Current.CancellationToken);

        var export = await JsonAsync(response);
        Assert.Equal(JsonValueKind.Null, export.GetProperty("household").ValueKind);
        Assert.Equal(0, export.GetProperty("members").GetArrayLength());
        Assert.Equal(0, export.GetProperty("sessions").GetArrayLength());
    }

    [Theory]
    [InlineData("GET", "/api/v1/me/data-export")]
    [InlineData("GET", "/api/v1/me/privacy-requests")]
    [InlineData("POST", "/api/v1/me/privacy-requests")]
    public async Task Staff_get_403_and_anonymous_callers_401_on_the_participant_routes(string method, string path)
    {
        using var staff = Factory.AsProgramAdministrator();
        using var anonymous = Factory.As(null);

        using var asStaff = await staff.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = Json("""{"type":"access"}""") }, TestContext.Current.CancellationToken);
        using var asAnonymous = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = Json("""{"type":"access"}""") }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_program_administrator_lists_open_requests_with_the_household_reference_and_closes_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        using var created = await RequestAsync(family.Client, new { type = "erasure" });
        var requestId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var subject = ApiClients.NewSubject("program-administrator");
        using var staff = Factory.AsProgramAdministrator(subject);

        var open = await ListAllAsync(staff, "open");
        using var closed = await staff.PostJsonAsync($"/api/v1/admin/privacy-requests/{requestId}/close", new { resolution = "completed" }, cancellationToken);
        using var twice = await staff.PostJsonAsync($"/api/v1/admin/privacy-requests/{requestId}/close", new { resolution = "declined" }, cancellationToken);
        var closedList = await ListAllAsync(staff, "closed");

        var listed = Assert.Single(open, item => item.GetProperty("id").GetGuid() == requestId);
        Assert.Equal(family.Household.Reference, listed.GetProperty("householdReference").GetString());
        Assert.Equal(family.Self, listed.GetProperty("memberId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var body = await JsonAsync(closed);
        Assert.Equal(("closed", "completed"), (body.GetProperty("status").GetString(), body.GetProperty("resolution").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("privacy.request_already_closed", await twice.ProblemCodeAsync(cancellationToken));
        Assert.Contains(closedList, item => item.GetProperty("id").GetGuid() == requestId);
        Assert.DoesNotContain(await ListAllAsync(staff, "open"), item => item.GetProperty("id").GetGuid() == requestId);
        Assert.Equal(1, await AuditCountAsync("privacy.request.closed", subject, requestId.ToString()));
        Assert.True(await AuditCountAsync("privacy.requests.read", subject) >= 1);
        using var mine = await family.Client.GetAsync("/api/v1/me/privacy-requests", cancellationToken);
        Assert.Equal("completed", (await JsonAsync(mine)).GetProperty("items")[0].GetProperty("resolution").GetString());
    }

    private static async Task<List<JsonElement>> ListAllAsync(HttpClient staff, string status)
    {
        var items = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            using var response = await staff.GetAsync($"/api/v1/admin/privacy-requests?status={status}&page={page}&pageSize=100", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await JsonAsync(response);
            items.AddRange(body.GetProperty("items").EnumerateArray());
            if (items.Count >= body.GetProperty("totalCount").GetInt32())
            {
                return items;
            }
        }
    }

    [Fact]
    public async Task Closing_refuses_an_unknown_request_and_an_unknown_resolution()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var staff = Factory.AsProgramAdministrator();
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        using var created = await RequestAsync(family.Client, new { type = "access" });
        var requestId = (await JsonAsync(created)).GetProperty("id").GetGuid();

        using var unknown = await staff.PostJsonAsync($"/api/v1/admin/privacy-requests/{Guid.NewGuid()}/close", new { resolution = "completed" }, cancellationToken);
        using var invalid = await staff.PostJsonAsync($"/api/v1/admin/privacy-requests/{requestId}/close", new { resolution = "ignored" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("privacy.request_not_found", await unknown.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Participant)]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Only_program_administrators_reach_the_staff_routes(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject(role), role);

        using var list = await client.GetAsync("/api/v1/admin/privacy-requests?status=open", cancellationToken);
        using var close = await client.PostJsonAsync($"/api/v1/admin/privacy-requests/{Guid.NewGuid()}/close", new { resolution = "completed" }, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
    }

    [Fact]
    public async Task Paging_the_staff_list_is_bounded()
    {
        using var staff = Factory.AsProgramAdministrator();

        using var response = await staff.GetAsync("/api/v1/admin/privacy-requests?pageSize=1000", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pageSize", await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Listing_my_requests_refuses_an_unbounded_page()
    {
        using var family = await Enrolment.CreateAsync(Factory, TestContext.Current.CancellationToken, Rosters.Head());

        using var response = await family.Client.GetAsync("/api/v1/me/privacy-requests?pageSize=1000", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("pageSize", await response.ErrorKeysAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Erasing_a_household_records_its_open_requests_as_closed_and_removes_every_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var family = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head());
        using var created = await RequestAsync(family.Client, new { type = "erasure" });
        using var handled = await RequestAsync(family.Client, new { type = "access" });
        var openId = (await JsonAsync(created)).GetProperty("id").GetString()!;
        var handledId = (await JsonAsync(handled)).GetProperty("id").GetString()!;
        using var staff = Factory.AsProgramAdministrator();
        using var closed = await staff.PostJsonAsync($"/api/v1/admin/privacy-requests/{handledId}/close", new { resolution = "completed" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var withdrawn = await family.Client.PatchJsonAsync($"/api/v1/households/{family.Household.Id}", new { status = "withdrawn" }, cancellationToken);
        await using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Erasure:ConsentRecords"] = "delete" })));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var erase = scope.ServiceProvider.GetRequiredService<EraseHousehold>();
            Assert.True((await erase.HandleAsync(family.Household.Id, family.Household.Reference, false, cancellationToken)).IsSuccess);
        }

        Assert.Equal(0, await Factory.QueryAsync(db => db.PrivacyRequests.CountAsync(
            request => request.HouseholdId == HouseholdId.From(family.Household.Id), cancellationToken)));
        Assert.Equal(1, await AuditCountAsync("privacy.request.closed", resourceId: openId));
        Assert.Equal(1, await AuditCountAsync("privacy.request.closed", resourceId: handledId));
    }
}
