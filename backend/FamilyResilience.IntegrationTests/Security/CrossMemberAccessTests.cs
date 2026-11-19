using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Consent;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Security;

/// <summary>
/// Inside one household, a member who signs in with an account of their own and the manager who answers for the others
/// cannot see each other's assignments, sessions, answers, history or consent (D-49). The manager-side refusals are in
/// LinkedMemberAccessTests; these are the member side and the lists.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class CrossMemberAccessTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    [Fact]
    public async Task A_member_with_an_account_cannot_reach_the_managers_assignment_session_or_answers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        var headSession = await Journeys.AnsweredSessionAsync(linked.Manager, api, linked.Manager.Self, "A", 0, cancellationToken);
        var headAssignment = await Journeys.AssignmentAsync(linked.Manager, linked.Manager.Self, "A", cancellationToken);

        var attempts = new[]
        {
            await linked.Parent.GetAsync($"/api/v1/assignments/{headAssignment}", cancellationToken),
            await linked.Parent.PostJsonAsync($"/api/v1/assignments/{headAssignment}/sessions", new { }, cancellationToken),
            await linked.Parent.GetAsync($"/api/v1/sessions/{headSession}", cancellationToken),
            await linked.Parent.GetAsync($"/api/v1/sessions/{headSession}/responses", cancellationToken),
            await linked.Parent.GetAsync($"/api/v1/sessions/{headSession}/result", cancellationToken),
            await Journeys.SubmitAsync(linked.Parent, headSession, Guid.NewGuid(), cancellationToken),
        };

        foreach (var attempt in attempts)
        {
            Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
            attempt.Dispose();
        }
    }

    [Fact]
    public async Task A_member_cannot_record_consent_for_the_manager_or_another_relative()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        var policy = await linked.Parent.CurrentPolicyAsync(ConsentType.Participation, cancellationToken);

        using var response = await linked.Parent.PostJsonAsync(
            "/api/v1/consent", new { memberId = linked.Manager.Self, policyVersionId = policy.Id, decision = "grant" }, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("households.member_not_found", await response.ProblemCodeAsync(cancellationToken));
    }

    [Fact]
    public async Task Lists_show_each_caller_only_the_members_they_may_act_for()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var linked = await Factory.LinkedParentAsync(cancellationToken);
        await Journeys.AnsweredSessionAsync(linked.Manager, api, linked.Manager.Self, "A", 0, cancellationToken);
        await linked.Parent.GrantConsentAsync(linked.ParentId, ConsentType.Participation, cancellationToken);

        var managerAssignments = await ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(linked.Manager.Client, "/api/v1/assignments");
        var parentAssignments = await ReadAsync<PagedResponse<ParticipantAssignmentResponse>>(linked.Parent, "/api/v1/assignments");
        var managerConsents = await ReadAsync<PagedResponse<ConsentRecordResponse>>(linked.Manager.Client, "/api/v1/me/consents");
        var parentConsents = await ReadAsync<PagedResponse<ConsentRecordResponse>>(linked.Parent, "/api/v1/me/consents");
        var parentHistory = await ReadAsync<PagedResponse<AssessmentHistoryItemResponse>>(linked.Parent, "/api/v1/me/assessment-history");

        Assert.DoesNotContain(managerAssignments.Items, assignment => assignment.MemberId == linked.ParentId);
        Assert.All(parentAssignments.Items, assignment => Assert.Equal(linked.ParentId, assignment.MemberId));
        Assert.NotEmpty(parentAssignments.Items);
        Assert.DoesNotContain(managerConsents.Items, consent => consent.MemberId == linked.ParentId);
        Assert.All(parentConsents.Items, consent => Assert.Equal(linked.ParentId, consent.MemberId));
        Assert.Empty(parentHistory.Items);
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<T>(TestContext.Current.CancellationToken);
    }
}
