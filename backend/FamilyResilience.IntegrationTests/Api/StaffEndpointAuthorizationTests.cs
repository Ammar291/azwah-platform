using System.Net;
using System.Net.Http.Json;
using FamilyResilience.Application.Identity;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Api;

/// <summary>Every assessment-engine endpoint, and every staff read the dashboard uses, is staff-only: anonymous callers get 401 and participants 403.</summary>
[Collection(ApiCollection.Name)]
public sealed class StaffEndpointAuthorizationTests(ApiFactory factory)
{
    public static TheoryData<string, string> StaffEndpoints => new()
    {
        { "POST", "/api/v1/instruments" },
        { "GET", "/api/v1/instruments" },
        { "GET", $"/api/v1/instruments/{Guid.Empty}" },
        { "PUT", $"/api/v1/instruments/{Guid.Empty}/text" },
        { "POST", $"/api/v1/instruments/{Guid.Empty}/versions" },
        { "GET", $"/api/v1/instrument-versions/{Guid.Empty}" },
        { "POST", $"/api/v1/instrument-versions/{Guid.Empty}/drafts" },
        { "POST", $"/api/v1/instrument-versions/{Guid.Empty}/approve" },
        { "POST", $"/api/v1/instrument-versions/{Guid.Empty}/schedule-end" },
        { "POST", $"/api/v1/instrument-versions/{Guid.Empty}/retire" },
        { "POST", $"/api/v1/instrument-versions/{Guid.Empty}/rule-sets" },
        { "GET", $"/api/v1/rule-sets/{Guid.Empty}" },
        { "POST", $"/api/v1/rule-sets/{Guid.Empty}/approve" },
        { "POST", $"/api/v1/rule-sets/{Guid.Empty}/retire" },
        { "POST", $"/api/v1/households/{Guid.Empty}/evaluate-eligibility" },
        { "POST", $"/api/v1/households/{Guid.Empty}/assign" },
        { "POST", $"/api/v1/assessment-sessions/{Guid.Empty}/score" },
        { "GET", $"/api/v1/assessment-sessions/{Guid.Empty}/score-results" },
        { "GET", "/api/v1/admin/waves" },
        { "GET", "/api/v1/admin/exports" },
        { "GET", "/api/v1/admin/exports/catalog" },
        { "GET", $"/api/v1/instruments/{Guid.Empty}/versions" },
        { "GET", $"/api/v1/instrument-versions/{Guid.Empty}/rule-sets" },
    };

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url) { Content = method == "GET" ? null : JsonContent.Create(new { }) };

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task An_anonymous_caller_gets_401(string method, string url)
    {
        using var client = factory.As(null);

        using var response = await client.SendAsync(Request(method, url), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(StaffEndpoints))]
    public async Task A_participant_gets_403(string method, string url)
    {
        using var client = factory.As("participant-subject", Roles.Participant);

        using var response = await client.SendAsync(Request(method, url), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
