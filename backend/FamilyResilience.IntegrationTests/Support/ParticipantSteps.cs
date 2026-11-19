using System.Net;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Domain.Consent;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Participant actions over HTTP, each asserting that it succeeded.</summary>
internal static class ParticipantSteps
{
    /// <summary>Creates the caller's household (the first roster entry marked self is the caller).</summary>
    public static async Task<HouseholdResponse> CreateHouseholdAsync(
        this HttpClient client, CancellationToken cancellationToken, params object[] roster)
    {
        using var response = await client.PostJsonAsync(
            "/api/v1/households", new { members = roster.Length == 0 ? [Rosters.Head()] : roster }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<HouseholdResponse>(cancellationToken);
    }

    /// <summary>The current policy of <paramref name="type"/>, as participants see it.</summary>
    public static async Task<ConsentPolicyResponse> CurrentPolicyAsync(
        this HttpClient client, ConsentType type, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/v1/consent/policies/current", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<CurrentConsentPoliciesResponse>(cancellationToken)).Policies.Single(policy => policy.ConsentType == type);
    }

    /// <summary>Grants the current policy of <paramref name="type"/> for <paramref name="memberId"/>.</summary>
    public static async Task<ConsentRecordResponse> GrantConsentAsync(
        this HttpClient client, Guid memberId, ConsentType type, CancellationToken cancellationToken)
    {
        var policy = await client.CurrentPolicyAsync(type, cancellationToken);
        using var response = await client.PostJsonAsync(
            "/api/v1/consent", new { memberId, policyVersionId = policy.Id, decision = "grant" }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ConsentRecordResponse>(cancellationToken);
    }

    public static async Task<IReadOnlyList<MemberResponse>> MembersAsync(
        this HttpClient client, Guid householdId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/v1/households/{householdId}/members?pageSize=100", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<PagedResponse<MemberResponse>>(cancellationToken)).Items;
    }
}
