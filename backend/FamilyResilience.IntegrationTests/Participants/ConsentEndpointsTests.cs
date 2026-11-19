using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class ConsentEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private static Task<HttpResponseMessage> DecideAsync(
        HttpClient client, Guid memberId, Guid policyId, string decision, CancellationToken cancellationToken) =>
        client.PostJsonAsync("/api/v1/consent", new { memberId, policyVersionId = policyId, decision }, cancellationToken);

    [Fact]
    public async Task The_current_policy_of_every_type_is_listed()
    {
        using var client = Factory.AsParticipant();

        using var response = await client.GetAsync("/api/v1/consent/policies/current", TestContext.Current.CancellationToken);

        var current = await response.ReadAsync<CurrentConsentPoliciesResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(Enum.GetValues<ConsentType>(), current.Policies.Select(policy => policy.ConsentType));
    }

    [Fact]
    public async Task Granting_records_in_app_consent_once_and_lists_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var client = enrolment.Client;
        var policy = await client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var first = await DecideAsync(client, enrolment.Self, policy.Id, "grant", cancellationToken);
        using var again = await DecideAsync(client, enrolment.Self, policy.Id, "grant", cancellationToken);
        using var listed = await client.GetAsync("/api/v1/me/consents", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var granted = await first.ReadAsync<ConsentRecordResponse>(cancellationToken);
        Assert.Equal(
            (enrolment.Self, ConsentType.Participation, policy.Id, policy.VersionNumber, ConsentCaptureMethod.InApp, true),
            (granted.MemberId, granted.ConsentType, granted.PolicyVersionId, granted.PolicyVersionNumber, granted.CaptureMethod, granted.IsActive));
        Assert.Equal(granted.Id, (await again.ReadAsync<ConsentRecordResponse>(cancellationToken)).Id);
        Assert.Equal(granted.Id, Assert.Single((await listed.ReadAsync<PagedResponse<ConsentRecordResponse>>(cancellationToken)).Items).Id);
        Assert.Equal(1, await Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "consent.consent.granted" && audit.ResourceId == granted.Id.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task The_manager_records_consent_for_a_member_without_an_account_but_not_for_a_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Spouse(), Rosters.Child(9));
        var policy = await enrolment.Client.CurrentPolicyAsync(ConsentType.ResearchUse, cancellationToken);

        using var forSpouse = await DecideAsync(enrolment.Client, enrolment.Aged(39), policy.Id, "grant", cancellationToken);
        using var forChild = await DecideAsync(enrolment.Client, enrolment.Aged(9), policy.Id, "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, forSpouse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, forChild.StatusCode);
        Assert.Equal("consent.member_under_18", await forChild.ProblemCodeAsync(cancellationToken));
    }

    // Only the birth month is stored, so a member whose 18th birthday falls this month may still be 17 (D-58).
    [Fact]
    public async Task Consent_is_not_recorded_for_a_member_who_turns_18_this_month()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(
            Factory,
            cancellationToken,
            Rosters.Head(),
            Rosters.Member(18, "child", "student", head: false, earner: false, self: false),
            Rosters.Member(19, "child", "student", head: false, earner: false, self: false));
        var policy = await enrolment.Client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var turning18 = await DecideAsync(enrolment.Client, enrolment.Aged(18), policy.Id, "grant", cancellationToken);
        using var aged19 = await DecideAsync(enrolment.Client, enrolment.Aged(19), policy.Id, "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, turning18.StatusCode);
        Assert.Equal("consent.member_under_18", await turning18.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, aged19.StatusCode);
    }

    [Fact]
    public async Task A_member_of_another_household_answers_404_and_gets_no_consent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await Enrolment.CreateAsync(Factory, cancellationToken);
        using var stranger = await Enrolment.CreateAsync(Factory, cancellationToken);
        var policy = await stranger.Client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var response = await DecideAsync(stranger.Client, owner.Self, policy.Id, "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("households.member_not_found", await response.ProblemCodeAsync(cancellationToken));
        Assert.Equal(0, await Factory.QueryAsync(db => db.ConsentRecords.CountAsync(
            consent => consent.FamilyMemberId == FamilyMemberId.From(owner.Self), cancellationToken)));
    }

    [Fact]
    public async Task An_unknown_policy_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);

        using var response = await DecideAsync(enrolment.Client, enrolment.Self, Guid.NewGuid(), "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("consent.policy_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Withdrawing_ends_the_consent_and_a_second_withdrawal_conflicts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var granted = await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Referral, cancellationToken);

        using var withdrawn = await DecideAsync(enrolment.Client, enrolment.Self, granted.PolicyVersionId, "withdraw", cancellationToken);
        using var twice = await DecideAsync(enrolment.Client, enrolment.Self, granted.PolicyVersionId, "withdraw", cancellationToken);

        var record = await withdrawn.ReadAsync<ConsentRecordResponse>(cancellationToken);
        Assert.Equal((granted.Id, false, (DateTimeOffset?)TestData.Now), (record.Id, record.IsActive, record.WithdrawnAt));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("consent.not_active", await twice.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task A_newer_policy_version_supersedes_the_older_grant_and_the_older_one_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var old = await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.ResearchUse, cancellationToken);
        var newer = await Factory.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken);

        using var stale = await DecideAsync(enrolment.Client, enrolment.Self, old.PolicyVersionId, "grant", cancellationToken);
        using var upgraded = await DecideAsync(enrolment.Client, enrolment.Self, newer.Id.Value, "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("consent.policy_not_current", await stale.ProblemCodeAsync(cancellationToken));
        var current = await upgraded.ReadAsync<ConsentRecordResponse>(cancellationToken);
        Assert.Equal((newer.Id.Value, newer.VersionNumber, true), (current.PolicyVersionId, current.PolicyVersionNumber, current.IsActive));
        var previous = await Factory.QueryAsync(db => db.ConsentRecords.SingleAsync(
            consent => consent.Id == ConsentRecordId.From(old.Id), cancellationToken));
        Assert.False(previous.IsActive);
    }

    [Fact]
    public async Task A_withdrawn_household_grants_nothing_new_but_may_still_withdraw()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var granted = await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        using var left = await enrolment.Client.PatchJsonAsync(
            $"/api/v1/households/{enrolment.Household.Id}", new { status = "withdrawn" }, cancellationToken);
        var research = await enrolment.Client.CurrentPolicyAsync(ConsentType.ResearchUse, cancellationToken);

        using var grant = await DecideAsync(enrolment.Client, enrolment.Self, research.Id, "grant", cancellationToken);
        using var withdraw = await DecideAsync(enrolment.Client, enrolment.Self, granted.PolicyVersionId, "withdraw", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, grant.StatusCode);
        Assert.Equal("households.not_active", await grant.ProblemCodeAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);
    }

    [Fact]
    public async Task Consent_pages_are_bounded_and_list_only_the_callers_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(), Rosters.Spouse());
        using var other = await Enrolment.CreateAsync(Factory, cancellationToken);
        await other.Client.GrantConsentAsync(other.Self, ConsentType.Participation, cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Self, ConsentType.Participation, cancellationToken);
        await enrolment.Client.GrantConsentAsync(enrolment.Aged(39), ConsentType.Participation, cancellationToken);

        using var page = await enrolment.Client.GetAsync("/api/v1/me/consents?page=2&pageSize=1", cancellationToken);
        using var tooBig = await enrolment.Client.GetAsync("/api/v1/me/consents?pageSize=101", cancellationToken);

        var second = await page.ReadAsync<PagedResponse<ConsentRecordResponse>>(cancellationToken);
        Assert.Equal((2, 1, 2), (second.Page, second.PageSize, second.TotalCount));
        Assert.Contains(Assert.Single(second.Items).MemberId, new[] { enrolment.Self, enrolment.Aged(39) });
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Contains("pageSize", await tooBig.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("""{"policyVersionId":"0198c6d6-0000-7000-8000-000000000001","decision":"grant"}""", "memberId")]
    [InlineData("""{"memberId":"0198c6d6-0000-7000-8000-000000000001","policyVersionId":"0198c6d6-0000-7000-8000-000000000001"}""", "decision")]
    [InlineData("""{"memberId":"0198c6d6-0000-7000-8000-000000000001","policyVersionId":"0198c6d6-0000-7000-8000-000000000001","decision":"maybe"}""", "$.decision")]
    public async Task An_invalid_decision_returns_400_keyed_by_the_field(string body, string key)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var response = await client.PostRawJsonAsync("/api/v1/consent", body, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.ErrorKeysAsync(cancellationToken));
    }

    [Theory]
    [InlineData("GET", "/api/v1/consent/policies/current")]
    [InlineData("POST", "/api/v1/consent")]
    [InlineData("GET", "/api/v1/me/consents")]
    public async Task Staff_get_403_and_anonymous_callers_401(string method, string url)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var staff = Factory.As(ApiClients.NewSubject("staff"), Roles.SystemAdministrator);
        using var anonymous = Factory.As(null);

        using var asStaff = await staff.SendAsync(Request(method, url), cancellationToken);
        using var asAnonymous = await anonymous.SendAsync(Request(method, url), cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, asStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : System.Net.Http.Json.JsonContent.Create(new { }),
        };

    // Enums travel by name only: a number in place of "grant" (1) or "withdraw" (2) is a malformed body, not a decision.
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("\"1\"")]
    [InlineData("0")]
    public async Task A_decision_sent_as_a_number_is_refused_and_nothing_is_recorded(string decision)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken);
        var policy = await enrolment.Client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var response = await enrolment.Client.PostRawJsonAsync(
            "/api/v1/consent",
            $$"""{"memberId":"{{enrolment.Self}}","policyVersionId":"{{policy.Id}}","decision":{{decision}}}""",
            cancellationToken);
        using var byName = await DecideAsync(enrolment.Client, enrolment.Self, policy.Id, "grant", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("$.decision", await response.ErrorKeysAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        Assert.Equal(1, await Factory.QueryAsync(db => db.ConsentRecords.CountAsync(
            consent => consent.FamilyMemberId == FamilyMemberId.From(enrolment.Self), cancellationToken)));
    }
}
