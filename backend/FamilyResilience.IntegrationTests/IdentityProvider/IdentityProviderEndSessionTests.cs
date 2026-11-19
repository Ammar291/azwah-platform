using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderEndSessionTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private static string EndSessionUrl(params (string Key, string? Value)[] parameters) =>
        QueryHelpers.AddQueryString("/end-session", parameters
            .Where(parameter => parameter.Value is not null)
            .Select(parameter => new KeyValuePair<string, string?>(parameter.Key, parameter.Value)));

    [Theory]
    [InlineData(IdentityProviderFlow.AdminClientId, "http://localhost:3000/sign-in?signedOut=1")]
    [InlineData(IdentityProviderFlow.MobileClientId, "azwah://auth/signed-out")]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://192.168.1.20:8081/--/auth/signed-out")]
    public async Task End_session_redirects_to_a_registered_post_logout_uri_with_the_state(string clientId, string postLogoutUri)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            EndSessionUrl(("client_id", clientId), ("post_logout_redirect_uri", postLogoutUri), ("state", "after-sign-out")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var redirect = AuthorizationResponse.From(response.Headers.Location!);
        Assert.Equal(postLogoutUri.Split('?')[0], redirect.Target);
        Assert.Equal("after-sign-out", redirect["state"]);
        Assert.StartsWith(postLogoutUri, response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task End_session_finds_the_client_from_the_id_token_hint()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "researcher", TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        using var response = await client.GetAsync(
            EndSessionUrl(("id_token_hint", signedIn.IdToken), ("post_logout_redirect_uri", "http://localhost:3000/sign-in?signedOut=1")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("http://localhost:3000/sign-in?signedOut=1", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData(IdentityProviderFlow.AdminClientId, "https://attacker.example/signed-out")]
    [InlineData(IdentityProviderFlow.AdminClientId, "http://localhost:3000/sign-in")]
    [InlineData(IdentityProviderFlow.AdminClientId, "azwah://auth/signed-out")]
    [InlineData(IdentityProviderFlow.MobileClientId, "http://localhost:3000/sign-in?signedOut=1")]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://192.168.1.20:8081/--/auth/callback")]
    [InlineData("someone-else", "http://localhost:3000/sign-in?signedOut=1")]
    [InlineData(null, "http://localhost:3000/sign-in?signedOut=1")]
    public async Task End_session_never_redirects_to_an_unregistered_uri(string? clientId, string postLogoutUri)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            EndSessionUrl(("client_id", clientId), ("post_logout_redirect_uri", postLogoutUri), ("state", "s")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task End_session_refuses_a_forged_id_token_hint()
    {
        using var client = factory.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "researcher", TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());
        var parts = signedIn.IdToken.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{IdentityProviderFlow.RandomValue()}";

        using var response = await client.GetAsync(
            EndSessionUrl(("id_token_hint", forged), ("post_logout_redirect_uri", "http://localhost:3000/sign-in?signedOut=1")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private static string EndSessionWithHint(string? postLogoutUri = "azwah://auth/signed-out") => EndSessionUrl(
        ("client_id", IdentityProviderFlow.MobileClientId),
        ("post_logout_redirect_uri", postLogoutUri),
        ("state", "s1"),
        ("idp_hint", "uae_pass"));

    [Fact]
    public async Task End_session_with_the_uae_pass_hint_goes_to_uae_pass_logout_then_back_to_the_client()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var endSession = await client.GetAsync(EndSessionWithHint(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, endSession.StatusCode);
        var logout = endSession.Headers.Location!;
        Assert.StartsWith("https://stg-id.uaepass.ae/idshub/logout?redirect_uri=", logout.OriginalString, StringComparison.Ordinal);
        var returnUri = QueryHelpers.ParseQuery(logout.Query)["redirect_uri"].ToString();
        Assert.StartsWith("http://localhost:5090/uaepass/signed-out?r=", returnUri, StringComparison.Ordinal);
        Assert.DoesNotContain("azwah://", logout.OriginalString, StringComparison.Ordinal);

        using var signedOut = await client.GetAsync(returnUri, TestContext.Current.CancellationToken);
        using var again = await client.GetAsync(returnUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, signedOut.StatusCode);
        Assert.Equal("azwah://auth/signed-out?state=s1", signedOut.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Null(again.Headers.Location);
    }

    [Fact]
    public async Task End_session_with_the_hint_but_uae_pass_disabled_redirects_straight_to_the_client()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EndSessionWithHint(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("azwah://auth/signed-out?state=s1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task End_session_with_the_hint_never_sends_an_unregistered_uri_through_uae_pass()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var response = await client.GetAsync(EndSessionWithHint("https://evil.example/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Signed_out_with_an_unknown_key_shows_the_page()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var response = await client.GetAsync("/uaepass/signed-out?r=nope", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("Signed out", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task End_session_without_a_redirect_shows_a_signed_out_page()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(EndSessionUrl(("client_id", IdentityProviderFlow.AdminClientId)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Signed out", html, StringComparison.Ordinal);
        Assert.Contains("simulated", html, StringComparison.Ordinal);
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
