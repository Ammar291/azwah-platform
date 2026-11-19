using System.Net;
using FamilyResilience.Api.Identity;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Households;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Participants;

[Collection(ParticipantApiCollection.Name)]
public sealed class MeEndpointsTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task A_participant_who_has_not_enrolled_has_no_household_and_may_create_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();

        using var me = await client.GetAsync("/api/v1/me", cancellationToken);
        using var permissions = await client.GetAsync("/api/v1/me/permissions", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Null((await me.ReadAsync<MeResponse>(cancellationToken)).Household);
        var granted = await permissions.ReadAsync<PermissionsResponse>(cancellationToken);
        Assert.Equal(
            ["assessments.answer", "assistant.use", "consent.manage", "households.create", "privacy.request", "questionnaires.read", "support.read", "support_resources.read"],
            granted.Permissions);
        Assert.Empty(granted.Households);
    }

    [Fact]
    public async Task After_enrolling_the_caller_sees_their_household_role_and_members()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.AsParticipant();
        var household = await client.CreateHouseholdAsync(cancellationToken, Rosters.Head(), Rosters.Spouse(), Rosters.Child(9));
        var members = await client.MembersAsync(household.Id, cancellationToken);

        using var me = await client.GetAsync("/api/v1/me", cancellationToken);
        using var permissions = await client.GetAsync("/api/v1/me/permissions", cancellationToken);

        var mine = (await me.ReadAsync<MeResponse>(cancellationToken)).Household;
        Assert.NotNull(mine);
        Assert.Equal(
            (household.Id, household.Reference, HouseholdRole.Manager, members.Single(member => member.IsCaller).Id),
            (mine.HouseholdId, mine.Reference, mine.Role, mine.MemberId));
        Assert.Equal(members.Select(member => member.Id).Order(), mine.ActableMemberIds.Order());
        var householdPermissions = Assert.Single((await permissions.ReadAsync<PermissionsResponse>(cancellationToken)).Households);
        Assert.Equal(household.Id, householdPermissions.HouseholdId);
        Assert.Equal(["household.read", "household.update", "members.manage", "roster.confirm"], householdPermissions.Permissions);
    }

    [Theory]
    [InlineData(Roles.Researcher, new[] { "analytics.read", "analytics.scores.read", "exports.manage", "questionnaires.read" })]
    [InlineData(Roles.ProgramAdministrator, new[] { "analytics.read", "assessments.monitor", "participation.read", "privacy.requests.manage", "questionnaires.read" })]
    [InlineData(Roles.SystemAdministrator, new[] { "content.manage", "eligibility.run", "partners.manage", "questionnaires.read", "results.read", "rules.manage", "safety_alerts.manage", "safety_alerts.read", "scoring.run", "support_resources.manage", "support_resources.read" })]
    [InlineData(Roles.SupportCoordinator, new[] { "questionnaires.read", "referrals.manage", "support_resources.read" })]
    public async Task Staff_permissions_follow_their_policies_and_never_include_participant_data(string role, string[] expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.As(ApiClients.NewSubject("staff"), role);

        using var response = await client.GetAsync("/api/v1/me/permissions", cancellationToken);

        var granted = await response.ReadAsync<PermissionsResponse>(cancellationToken);
        Assert.Equal(expected, granted.Permissions);
        Assert.Empty(granted.Households);
    }

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/me/permissions")]
    public async Task An_anonymous_caller_gets_401(string url)
    {
        using var client = Factory.As(null);

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
