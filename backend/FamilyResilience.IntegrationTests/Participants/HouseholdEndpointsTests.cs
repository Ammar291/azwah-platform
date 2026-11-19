using System.Net;
using System.Text.RegularExpressions;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed partial class HouseholdEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task Creating_a_household_returns_201_with_its_location_and_links_the_self_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.PostJsonAsync(
            "/api/v1/households", new { members = new[] { Rosters.Head(), Rosters.Spouse(), Rosters.Child(9) } }, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var household = await response.ReadAsync<HouseholdResponse>(cancellationToken);
        Assert.Equal($"/api/v1/households/{household.Id}", response.Headers.Location?.OriginalString);
        Assert.Matches(ReferenceFormat(), household.Reference);
        Assert.Equal((HouseholdStatus.Active, 3, (DateTimeOffset?)null), (household.Status, household.ActiveMemberCount, household.RosterConfirmedAt));
        var members = await client.MembersAsync(household.Id, cancellationToken);
        var self = Assert.Single(members, member => member.IsCaller);
        Assert.True(self.IsHeadOfHousehold);
        Assert.True(self.HasAccount);
        Assert.Equal(42, self.Age);
        Assert.All(members.Where(member => !member.IsCaller), member => Assert.False(member.HasAccount));
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "households.household.created" && audit.ResourceId == household.Id.ToString(), cancellationToken)));
    }

    public static TheoryData<string, string> InvalidRosters => new()
    {
        { """{"members":[]}""", "members" },
        { """{}""", "members" },
        { """{"members":[{"birthYear":1984,"birthMonth":10,"relationshipToHousehold":null,"employmentStatus":"employed","isHeadOfHousehold":true,"isPrimaryIncomeEarner":true,"isSelf":false}]}""", "members" },
        { """{"members":[{"birthYear":1984,"birthMonth":13,"relationshipToHousehold":null,"employmentStatus":"employed","isHeadOfHousehold":true,"isPrimaryIncomeEarner":true,"isSelf":true}]}""", "members[0].birthMonth" },
        { """{"members":[{"birthYear":1984,"birthMonth":10,"relationshipToHousehold":null,"employmentStatus":"employed","isPrimaryIncomeEarner":true,"isSelf":true}]}""", "members[0].isHeadOfHousehold" },
        { """{"members":[{"birthYear":1984,"birthMonth":10,"relationshipToHousehold":"spouse","employmentStatus":"employed","isHeadOfHousehold":true,"isPrimaryIncomeEarner":true,"isSelf":true}]}""", "members[0].relationshipToHousehold" },
        { """{"members":[{"birthYear":1984,"birthMonth":10,"relationshipToHousehold":null,"employmentStatus":"astronaut","isHeadOfHousehold":true,"isPrimaryIncomeEarner":true,"isSelf":true}]}""", "$.members[0].employmentStatus" },
    };

    [Theory]
    [MemberData(nameof(InvalidRosters))]
    public async Task An_invalid_roster_returns_400_keyed_by_the_field(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.PostRawJsonAsync("/api/v1/households", body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task A_birth_month_in_the_future_returns_400_with_its_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.PostJsonAsync(
            "/api/v1/households", new { members = new[] { Rosters.Head(), Rosters.Child(-1) } }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("common.year_month_in_future", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_caller_under_18_cannot_manage_a_household()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.PostJsonAsync(
            "/api/v1/households",
            new { members = new[] { Rosters.Head(self: false), Rosters.Member(17, "child", "student", head: false, earner: false, self: true) } },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.manager_under_18", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_caller_can_belong_to_one_household_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        await client.CreateHouseholdAsync(cancellationToken);

        using var second = await client.PostJsonAsync("/api/v1/households", new { members = new[] { Rosters.Head() } }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("households.already_member", await second.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_double_tapped_enrolment_creates_one_household_and_answers_the_other_as_already_a_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var body = new { members = new[] { Rosters.Head() } };

        var responses = await Task.WhenAll(
            client.PostJsonAsync("/api/v1/households", body, cancellationToken),
            client.PostJsonAsync("/api/v1/households", body, cancellationToken));

        try
        {
            Assert.Equal(
                [HttpStatusCode.Created, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
            var refused = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("households.already_member", await refused.ProblemCodeAsync(cancellationToken));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    public static TheoryData<string, string> ManagerEndpoints => new()
    {
        { "GET", "" },
        { "PATCH", "" },
        { "GET", "/members" },
        { "POST", "/members" },
        { "GET", "/members/{member}" },
        { "PATCH", "/members/{member}" },
    };

    private static HttpRequestMessage Request(string method, string url, object? body = null) =>
        new(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : System.Net.Http.Json.JsonContent.Create(body ?? new { }, options: ApiJson.Options),
        };

    private static object BodyFor(string method, string path) => (method, path) switch
    {
        ("PATCH", "") => new { status = "withdrawn" },
        ("POST", "/members") => Rosters.Child(5),
        _ => new { employmentStatus = "retired" },
    };

    [Theory]
    [MemberData(nameof(ManagerEndpoints))]
    public async Task Another_familys_household_answers_404_and_changes_nothing(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.AsParticipant();
        var household = await owner.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await owner.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);
        using var stranger = Factory.AsParticipant();
        await stranger.CreateHouseholdAsync(cancellationToken);
        var url = $"/api/v1/households/{household.Id}{path.Replace("{member}", spouse.Id.ToString(), StringComparison.Ordinal)}";

        using var response = await stranger.SendAsync(Request(method, url, BodyFor(method, path)), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("households.household_not_found", await response.ProblemCodeAsync(cancellationToken));
        var unchanged = await owner.MembersAsync(household.Id, cancellationToken);
        Assert.Equal(2, unchanged.Count);
        Assert.Equal(EmploymentStatus.Employed, unchanged.Single(member => member.Id == spouse.Id).EmploymentStatus);
        using var mine = await owner.GetAsync($"/api/v1/households/{household.Id}", cancellationToken);
        Assert.Equal(HouseholdStatus.Active, (await mine.ReadAsync<HouseholdResponse>(cancellationToken)).Status);
    }

    [Theory]
    [MemberData(nameof(ManagerEndpoints))]
    public async Task A_household_that_does_not_exist_answers_404(string method, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        await client.CreateHouseholdAsync(cancellationToken);
        var url = $"/api/v1/households/{Guid.NewGuid()}{path.Replace("{member}", Guid.NewGuid().ToString(), StringComparison.Ordinal)}";

        using var response = await client.SendAsync(Request(method, url, BodyFor(method, path)), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("households.household_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Theory]
    [InlineData(Roles.Researcher)]
    [InlineData(Roles.ProgramAdministrator)]
    [InlineData(Roles.SystemAdministrator)]
    public async Task Staff_roles_get_403_on_household_endpoints(string role)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.AsParticipant();
        var household = await owner.CreateHouseholdAsync(cancellationToken);
        using var staff = Factory.As(ApiClients.NewSubject("staff"), role);

        using var create = await staff.PostJsonAsync("/api/v1/households", new { members = new[] { Rosters.Head() } }, cancellationToken);
        using var read = await staff.GetAsync($"/api/v1/households/{household.Id}", cancellationToken);
        using var members = await staff.GetAsync($"/api/v1/households/{household.Id}/members", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, members.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ManagerEndpoints))]
    public async Task An_anonymous_caller_gets_401(string method, string path)
    {
        using var client = Factory.As(null);
        var url = $"/api/v1/households/{Guid.NewGuid()}{path.Replace("{member}", Guid.NewGuid().ToString(), StringComparison.Ordinal)}";

        using var response = await client.SendAsync(Request(method, url, BodyFor(method, path)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_member_returns_201_with_its_location()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);

        using var response = await client.PostJsonAsync($"/api/v1/households/{household.Id}/members", Rosters.Child(6), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = await response.ReadAsync<MemberResponse>(cancellationToken);
        Assert.Equal($"/api/v1/households/{household.Id}/members/{added.Id}", response.Headers.Location?.OriginalString);
        using var fetched = await client.GetAsync(response.Headers.Location!.OriginalString, cancellationToken);
        Assert.Equal((HouseholdRelationship.Child, 6, MemberStatus.Active), ((await fetched.ReadAsync<MemberResponse>(cancellationToken)).RelationshipToHousehold, added.Age, added.Status));
    }

    [Fact]
    public async Task A_roster_of_more_than_30_members_returns_400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        object[] roster = [Rosters.Head(), .. Enumerable.Range(0, Household.MaxActiveMembers).Select(_ => Rosters.Child(9))];

        using var response = await client.PostJsonAsync("/api/v1/households", new { members = roster }, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("members", await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Adding_a_member_to_a_full_roster_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(
            cancellationToken, [Rosters.Head(), .. Enumerable.Range(0, Household.MaxActiveMembers - 1).Select(_ => Rosters.Child(9))]);

        using var response = await client.PostJsonAsync($"/api/v1/households/{household.Id}/members", Rosters.Child(4), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.roster_full", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(Household.MaxActiveMembers, (await client.MembersAsync(household.Id, cancellationToken)).Count);
    }

    [Fact]
    public async Task A_second_head_returns_409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);

        using var response = await client.PostJsonAsync(
            $"/api/v1/households/{household.Id}/members", Rosters.Member(50, null, "employed", head: true, earner: false, self: false), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.head_already_exists", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_merge_patch_changes_only_the_fields_sent_and_null_clears()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);
        var url = $"/api/v1/households/{household.Id}/members/{spouse.Id}";

        using var retired = await client.PatchJsonAsync(url, new { employmentStatus = "retired" }, cancellationToken);
        using var cleared = await client.PatchRawJsonAsync(url, """{"relationshipToHousehold":null}""", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var afterRetired = await retired.ReadAsync<MemberResponse>(cancellationToken);
        Assert.Equal((EmploymentStatus.Retired, HouseholdRelationship.Spouse, spouse.BirthYear), (afterRetired.EmploymentStatus, afterRetired.RelationshipToHousehold, afterRetired.BirthYear));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var afterCleared = await cleared.ReadAsync<MemberResponse>(cancellationToken);
        Assert.Equal((EmploymentStatus.Retired, (HouseholdRelationship?)null), (afterCleared.EmploymentStatus, afterCleared.RelationshipToHousehold));
    }

    public static TheoryData<string, string> InvalidPatches => new()
    {
        { "{}", "request" },
        { """{"status":"active"}""", "status" },
        { """{"status":"inactive","employmentStatus":"retired"}""", "status" },
        { """{"birthMonth":0}""", "birthMonth" },
        { """{"birthYear":null}""", "$.birthYear" },
        { """{"isHeadOfHousehold":null}""", "$.isHeadOfHousehold" },
    };

    [Theory]
    [MemberData(nameof(InvalidPatches))]
    public async Task An_invalid_patch_returns_400_keyed_by_the_field(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);

        using var response = await client.PatchRawJsonAsync($"/api/v1/households/{household.Id}/members/{spouse.Id}", body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Removing_a_member_keeps_them_as_inactive_history_but_the_manager_stays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var members = await client.MembersAsync(household.Id, cancellationToken);
        var spouse = members.Single(member => !member.IsCaller);
        var self = members.Single(member => member.IsCaller);

        using var removed = await client.PatchJsonAsync($"/api/v1/households/{household.Id}/members/{spouse.Id}", new { status = "inactive" }, cancellationToken);
        using var manager = await client.PatchJsonAsync($"/api/v1/households/{household.Id}/members/{self.Id}", new { status = "inactive" }, cancellationToken);
        using var again = await client.PatchJsonAsync($"/api/v1/households/{household.Id}/members/{spouse.Id}", new { employmentStatus = "retired" }, cancellationToken);

        Assert.Equal(MemberStatus.Inactive, (await removed.ReadAsync<MemberResponse>(cancellationToken)).Status);
        Assert.Equal(HttpStatusCode.Conflict, manager.StatusCode);
        Assert.Equal("households.member_is_manager", await manager.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("households.member_not_found", await again.ProblemCodeAsync(cancellationToken));
        Assert.Equal(2, (await client.MembersAsync(household.Id, cancellationToken)).Count);
    }

    [Fact]
    public async Task The_manager_cannot_be_made_younger_than_18()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(self: false), Rosters.Spouse(self: true));
        var self = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => member.IsCaller);

        using var response = await client.PatchJsonAsync(
            $"/api/v1/households/{household.Id}/members/{self.Id}", new { birthYear = TestData.Today.Year - 17 }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.manager_under_18", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(39, (await client.MembersAsync(household.Id, cancellationToken)).Single(member => member.IsCaller).Age);
    }

    [Fact]
    public async Task A_member_with_active_consent_cannot_be_recorded_as_under_18()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);
        await client.GrantConsentAsync(spouse.Id, Domain.Consent.ConsentType.Participation, cancellationToken);

        using var response = await client.PatchJsonAsync(
            $"/api/v1/households/{household.Id}/members/{spouse.Id}", new { birthYear = TestData.Today.Year - 17 }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.member_has_consent", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(39, (await client.MembersAsync(household.Id, cancellationToken)).Single(member => member.Id == spouse.Id).Age);
    }

    [Fact]
    public async Task A_member_with_active_consent_cannot_be_corrected_to_turning_18_this_month()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);
        await client.GrantConsentAsync(spouse.Id, Domain.Consent.ConsentType.Participation, cancellationToken);

        using var response = await client.PatchJsonAsync(
            $"/api/v1/households/{household.Id}/members/{spouse.Id}", new { birthYear = TestData.Today.Year - 18 }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.member_has_consent", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_member_without_consent_can_be_corrected_to_under_18()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse());
        var spouse = (await client.MembersAsync(household.Id, cancellationToken)).Single(member => !member.IsCaller);

        using var response = await client.PatchJsonAsync(
            $"/api/v1/households/{household.Id}/members/{spouse.Id}",
            new { birthYear = TestData.Today.Year - 17, relationshipToHousehold = "child", employmentStatus = (string?)null },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(17, (await response.ReadAsync<MemberResponse>(cancellationToken)).Age);
    }

    [Fact]
    public async Task A_member_of_another_household_is_not_found_through_mine()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var other = Factory.AsParticipant();
        var theirs = await other.CreateHouseholdAsync(cancellationToken);
        var theirMember = Assert.Single(await other.MembersAsync(theirs.Id, cancellationToken));
        using var client = Factory.AsParticipant();
        var mine = await client.CreateHouseholdAsync(cancellationToken);

        using var read = await client.GetAsync($"/api/v1/households/{mine.Id}/members/{theirMember.Id}", cancellationToken);
        using var patch = await client.PatchJsonAsync($"/api/v1/households/{mine.Id}/members/{theirMember.Id}", new { employmentStatus = "retired" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
        Assert.Equal("households.member_not_found", await patch.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Withdrawing_freezes_the_roster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);

        using var withdrawn = await client.PatchJsonAsync($"/api/v1/households/{household.Id}", new { status = "withdrawn" }, cancellationToken);
        using var twice = await client.PatchJsonAsync($"/api/v1/households/{household.Id}", new { status = "withdrawn" }, cancellationToken);
        using var add = await client.PostJsonAsync($"/api/v1/households/{household.Id}/members", Rosters.Child(3), cancellationToken);
        using var reactivate = await client.PatchJsonAsync($"/api/v1/households/{household.Id}", new { status = "active" }, cancellationToken);

        Assert.Equal(HouseholdStatus.Withdrawn, (await withdrawn.ReadAsync<HouseholdResponse>(cancellationToken)).Status);
        Assert.Equal("households.already_withdrawn", await twice.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        Assert.Equal("households.not_active", await add.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, reactivate.StatusCode);
        Assert.Contains("status", await reactivate.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?page=0", "page")]
    public async Task Member_pages_are_bounded(string query, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken);

        using var response = await client.GetAsync($"/api/v1/households/{household.Id}/members{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Fact]
    public async Task Members_come_a_page_at_a_time_in_roster_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse(), Rosters.Child(9));
        var all = await client.MembersAsync(household.Id, cancellationToken);

        using var response = await client.GetAsync($"/api/v1/households/{household.Id}/members?page=2&pageSize=2", cancellationToken);

        var page = await response.ReadAsync<PagedResponse<MemberResponse>>(cancellationToken);
        Assert.Equal((2, 2, 3), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal(all[2].Id, Assert.Single(page.Items).Id);
    }

    [GeneratedRegex(@"^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$")]
    private static partial Regex ReferenceFormat();
}
