using System.Net;
using System.Net.Http.Json;
using FamilyResilience.IntegrationTests.Support;

namespace FamilyResilience.IntegrationTests.Security;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(ApiFactory factory)
{
    private HttpClient ClientAs(string? subject, params string[] roles)
    {
        var client = factory.CreateClient();
        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, subject);
        }

        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

    [Fact]
    public async Task Me_rejects_an_anonymous_caller_with_a_401_problem()
    {
        using var client = ClientAs(null);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_returns_the_callers_subject_and_sorted_roles()
    {
        using var client = ClientAs("subject-42", "researcher", "participant");

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("subject-42", me!.Subject);
        Assert.Equal(new[] { "participant", "researcher" }, me.Roles);
    }

    [Fact]
    public async Task Role_policy_forbids_the_wrong_role()
    {
        using var client = ClientAs("subject-42", "participant");

        var response = await client.GetAsync("/test-only/researcher-only", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Role_policy_allows_the_right_role()
    {
        using var client = ClientAs("subject-42", "researcher");

        var response = await client.GetAsync("/test-only/researcher-only", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_stay_anonymous(string path)
    {
        using var client = ClientAs(null);

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record MeBody(string Subject, string[] Roles);
}
