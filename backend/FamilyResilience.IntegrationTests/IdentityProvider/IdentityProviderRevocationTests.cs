using System.Net;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderRevocationTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    [Fact]
    public async Task Revoking_a_refresh_token_ends_its_family()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "system_administrator", TestContext.Current.CancellationToken, ClientCredentials.AdminPost());

        using var revoked = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.AdminPost(), signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var refreshed = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.AdminPost(), signedIn.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
        Assert.Equal("invalid_grant", await IdentityProviderFlow.ReadErrorAsync(refreshed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_public_client_can_revoke_its_own_refresh_token()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "participant_member", TestContext.Current.CancellationToken);

        using var revoked = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var refreshed = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
    }

    [Fact]
    public async Task Revocation_answers_200_for_unknown_access_and_already_revoked_tokens()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);
        using var first = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);

        using var again = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var unknown = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.Mobile, IdentityProviderFlow.RandomValue(), TestContext.Current.CancellationToken);
        using var access = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.Mobile, signedIn.AccessToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        Assert.Equal("no-store", again.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Another_client_cannot_revoke_a_refresh_token_it_was_not_issued()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(client, "researcher", TestContext.Current.CancellationToken);

        using var revoked = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.AdminBasic(), signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var refreshed = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task Revocation_by_the_confidential_client_needs_its_secret()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "researcher", TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        using var revoked = await IdentityProviderFlow.RevokeAsync(
            client, ClientCredentials.AdminBasic("wrong"), signedIn.RefreshToken, TestContext.Current.CancellationToken);
        using var refreshed = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.AdminBasic(), signedIn.RefreshToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("invalid_client", await IdentityProviderFlow.ReadErrorAsync(revoked, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }
}
