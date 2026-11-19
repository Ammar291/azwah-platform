using System.Net;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// D-49: a member who signs in themselves is reachable only by themselves, never by the household manager. Joining has
/// no endpoint yet, so the member's account is linked straight in the database.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class LinkedMemberAccessTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private async Task<(Enrolment Manager, HttpClient Parent, Guid ParentId, Guid ParentAssignment)> LinkedParentAsync(CancellationToken cancellationToken)
    {
        var linked = await Factory.LinkedParentAsync(cancellationToken);
        return (linked.Manager, linked.Parent, linked.ParentId, linked.ParentAssignment);
    }

    [Fact]
    public async Task The_manager_can_no_longer_reach_a_member_who_signs_in_themselves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (manager, parent, parentId, assignmentId) = await LinkedParentAsync(cancellationToken);
        using var _ = manager;
        using var __ = parent;
        var policy = await manager.Client.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var me = await manager.Client.GetAsync("/api/v1/me", cancellationToken);
        using var assignment = await manager.Client.GetAsync($"/api/v1/assignments/{assignmentId}", cancellationToken);
        using var start = await manager.Client.PostJsonAsync($"/api/v1/assignments/{assignmentId}/sessions", new { }, cancellationToken);
        using var consent = await manager.Client.PostJsonAsync(
            "/api/v1/consent", new { memberId = parentId, policyVersionId = policy.Id, decision = "grant" }, cancellationToken);

        Assert.DoesNotContain(parentId, (await me.ReadAsync<MeResponse>(cancellationToken)).Household!.ActableMemberIds);
        Assert.Equal(HttpStatusCode.NotFound, assignment.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, consent.StatusCode);
        Assert.Equal("households.member_not_found", await consent.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task The_member_answers_for_themselves_and_the_manager_cannot_see_their_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (manager, parent, parentId, assignmentId) = await LinkedParentAsync(cancellationToken);
        using var _ = manager;
        using var __ = parent;

        using var me = await parent.GetAsync("/api/v1/me", cancellationToken);
        await parent.GrantConsentAsync(parentId, ConsentType.Participation, cancellationToken);
        var session = await Journeys.StartAsync(parent, assignmentId, cancellationToken);
        using var asManager = await manager.Client.GetAsync($"/api/v1/sessions/{session.Id}", cancellationToken);
        using var roster = await manager.Client.GetAsync($"/api/v1/households/{manager.Household.Id}/members", cancellationToken);
        using var asMember = await parent.GetAsync($"/api/v1/households/{manager.Household.Id}", cancellationToken);

        var mine = (await me.ReadAsync<MeResponse>(cancellationToken)).Household!;
        Assert.Equal((HouseholdRole.Member, parentId), (mine.Role, mine.MemberId));
        Assert.Equal([parentId], mine.ActableMemberIds);
        Assert.Equal(HttpStatusCode.NotFound, asManager.StatusCode);
        var parentRow = (await roster.ReadAsync<Application.Abstractions.PagedResponse<MemberResponse>>(cancellationToken)).Items
            .Single(member => member.Id == parentId);
        Assert.True(parentRow.HasAccount);
        Assert.Equal(HttpStatusCode.NotFound, asMember.StatusCode);
    }

    [Fact]
    public async Task The_manager_cannot_make_a_member_with_their_own_account_younger_than_18()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (manager, parent, parentId, _) = await LinkedParentAsync(cancellationToken);
        using var _ = manager;
        using var __ = parent;

        using var response = await manager.Client.PatchJsonAsync(
            $"/api/v1/households/{manager.Household.Id}/members/{parentId}", new { birthYear = TestData.Today.Year - 17 }, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("households.account_holder_under_18", await response.ProblemCodeAsync(cancellationToken));
    }
}
