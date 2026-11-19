using System.Net;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderRefreshTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private static async Task<string> NewRefreshTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        return tokens.GetProperty("refresh_token").GetString()!;
    }

    private static async Task AssertInvalidGrantAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refresh_rotates_the_refresh_token_and_issues_a_new_access_token()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "support_coordinator", TestContext.Current.CancellationToken);

        using var response = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var tokens = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.Equal(600, tokens.GetProperty("expires_in").GetInt32());
        Assert.Equal("openid offline_access", tokens.GetProperty("scope").GetString());
        Assert.NotEqual(signedIn.RefreshToken, tokens.GetProperty("refresh_token").GetString());
        var access = tokens.GetProperty("access_token").GetString()!;
        Assert.NotEqual(signedIn.AccessToken, access);
        var claims = TestJwt.Payload(access);
        Assert.Equal("dev-support-coordinator", claims.GetProperty("sub").GetString());
        Assert.Equal(new[] { "support_coordinator" }, claims.GetProperty("role").EnumerateArray().Select(role => role.GetString()!));
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_family()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        using var rotation = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        var second = await NewRefreshTokenAsync(rotation);

        using var reuse = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var afterReuse = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, second, TestContext.Current.CancellationToken);

        await AssertInvalidGrantAsync(reuse);
        await AssertInvalidGrantAsync(afterReuse);
    }

    [Fact]
    public async Task Reuse_revokes_only_its_own_family()
    {
        using var client = factory.CreateClient();
        var stolen = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        var other = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        using var rotation = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, stolen.RefreshToken, TestContext.Current.CancellationToken);
        using var reuse = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, stolen.RefreshToken, TestContext.Current.CancellationToken);

        using var response = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, other.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_refresh_token_of_another_client_is_an_invalid_grant_and_stays_usable_by_its_client()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);

        using var foreign = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.AdminBasic(), signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var own = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);

        await AssertInvalidGrantAsync(foreign);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_an_invalid_grant()
    {
        using var client = factory.CreateClient();

        using var response = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, IdentityProviderFlow.RandomValue(), TestContext.Current.CancellationToken);

        await AssertInvalidGrantAsync(response);
    }

    [Fact]
    public async Task A_refresh_without_the_token_is_an_invalid_request()
    {
        using var client = factory.CreateClient();
        using var request = IdentityProviderFlow.ClientRequest("/token", ClientCredentials.Mobile, ("grant_type", "refresh_token"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refresh_cannot_widen_the_scope()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        using var request = IdentityProviderFlow.ClientRequest("/token", ClientCredentials.Mobile,
            ("grant_type", "refresh_token"), ("refresh_token", signedIn.RefreshToken), ("scope", "openid offline_access admin"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_scope", await IdentityProviderFlow.ReadErrorAsync(response, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Refresh_tokens_end_eight_hours_after_sign_in_however_often_they_rotate()
    {
        await using var provider = new IdentityProviderFactory();
        using var client = provider.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        provider.Clock.Advance(TimeSpan.FromHours(7));
        using var rotation = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        var rotated = await NewRefreshTokenAsync(rotation);
        provider.Clock.Advance(TimeSpan.FromHours(1));

        using var response = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, rotated, TestContext.Current.CancellationToken);

        await AssertInvalidGrantAsync(response);
    }
}
