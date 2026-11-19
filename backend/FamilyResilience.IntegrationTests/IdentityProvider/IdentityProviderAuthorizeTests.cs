using System.Net;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderAuthorizeTests(IdentityProviderFactory factory) : IClassFixture<IdentityProviderFactory>
{
    private const string PageSecurityPolicyStart = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'";

    private static readonly string[] PersonaCodes =
    [
        "participant_lead",
        "participant_member",
        "participant_new",
        "researcher",
        "program_administrator",
        "system_administrator",
        "support_coordinator",
    ];

    public static TheoryData<string> Personas => [.. PersonaCodes];

    private static async Task AssertErrorPageWithoutRedirectAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Development identity provider", html, StringComparison.Ordinal);
        Assert.Contains("simulated", html, StringComparison.Ordinal);
    }

    private static AuthorizationResponse AssertRedirect(HttpResponseMessage response, string expectedTarget)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var redirect = AuthorizationResponse.From(response.Headers.Location!);
        Assert.Equal(expectedTarget, redirect.Target);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        return redirect;
    }

    [Fact]
    public async Task An_unknown_client_gets_an_error_page_and_is_never_redirected()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["client_id"] = "someone-else";

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        await AssertErrorPageWithoutRedirectAsync(response);
    }

    [Theory]
    [InlineData(IdentityProviderFlow.MobileClientId, null)]
    [InlineData(IdentityProviderFlow.MobileClientId, "https://attacker.example/callback")]
    [InlineData(IdentityProviderFlow.MobileClientId, "azwah://auth/callback/extra")]
    [InlineData(IdentityProviderFlow.MobileClientId, "AZWAH://auth/callback")]
    [InlineData(IdentityProviderFlow.MobileClientId, IdentityProviderFlow.AdminRedirectUri)]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://192.168.1.20:8081/--/auth/elsewhere")]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://192.168.1.20:8081/--/auth/callback#fragment")]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://192.168.1.20:8081/--/auth/callback?next=https://attacker.example")]
    [InlineData(IdentityProviderFlow.AdminClientId, IdentityProviderFlow.MobileRedirectUri)]
    [InlineData(IdentityProviderFlow.AdminClientId, "http://localhost:3000/auth/callback/")]
    [InlineData(IdentityProviderFlow.AdminClientId, "http://localhost:3001/auth/callback")]
    [InlineData(IdentityProviderFlow.AdminClientId, IdentityProviderFlow.ExpoGoRedirectUri)]
    public async Task An_unregistered_redirect_uri_gets_an_error_page_and_is_never_redirected(string clientId, string? redirectUri)
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.AuthorizeParameters(clientId, redirectUri!, Pkce.Create());

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        await AssertErrorPageWithoutRedirectAsync(response);
    }

    [Theory]
    [InlineData(IdentityProviderFlow.MobileClientId, IdentityProviderFlow.MobileRedirectUri)]
    [InlineData(IdentityProviderFlow.MobileClientId, IdentityProviderFlow.ExpoGoRedirectUri)]
    [InlineData(IdentityProviderFlow.MobileClientId, "exp://u.expo.dev/project-id/--/auth/callback")]
    [InlineData(IdentityProviderFlow.AdminClientId, IdentityProviderFlow.AdminRedirectUri)]
    public async Task A_registered_redirect_uri_gets_the_persona_page(string clientId, string redirectUri)
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.AuthorizeParameters(clientId, redirectUri, Pkce.Create());

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("state", null, "invalid_request")]
    [InlineData("nonce", null, "invalid_request")]
    [InlineData("code_challenge", null, "invalid_request")]
    [InlineData("code_challenge", "too-short", "invalid_request")]
    [InlineData("code_challenge_method", null, "invalid_request")]
    [InlineData("code_challenge_method", "plain", "invalid_request")]
    [InlineData("response_type", null, "invalid_request")]
    [InlineData("response_type", "token", "unsupported_response_type")]
    [InlineData("response_type", "code id_token", "unsupported_response_type")]
    [InlineData("scope", null, "invalid_scope")]
    [InlineData("scope", "offline_access", "invalid_scope")]
    [InlineData("response_mode", "fragment", "invalid_request")]
    [InlineData("prompt", "none", "login_required")]
    [InlineData("request", "eyJhbGciOiJub25lIn0.e30.", "request_not_supported")]
    [InlineData("request_uri", "https://attacker.example/request", "request_uri_not_supported")]
    public async Task An_invalid_request_from_a_registered_client_redirects_with_the_error_state_and_iss(
        string parameter, string? value, string expectedError)
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters[parameter] = value;

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        var redirect = AssertRedirect(response, IdentityProviderFlow.MobileRedirectUri);
        Assert.Equal(expectedError, redirect["error"]);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Null(redirect["code"]);
    }

    [Fact]
    public async Task A_repeated_parameter_redirects_with_invalid_request()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        using var response = await client.GetAsync(
            $"{IdentityProviderFlow.AuthorizeUrl(parameters)}&nonce=second", TestContext.Current.CancellationToken);

        var redirect = AssertRedirect(response, IdentityProviderFlow.MobileRedirectUri);
        Assert.Equal("invalid_request", redirect["error"]);
    }

    [Fact]
    public async Task The_persona_page_is_labelled_simulated_and_offers_every_persona()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Development identity provider — simulated. No identity is checked.", html, StringComparison.Ordinal);
        foreach (var persona in PersonaCodes)
        {
            Assert.Contains($"value=\"{persona}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("name=\"cancel\"", html, StringComparison.Ordinal);
        Assert.StartsWith(PageSecurityPolicyStart, Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.EndsWith("; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_persona_page_lets_the_form_return_only_to_the_validated_redirect_target()
    {
        // Browsers apply form-action to the redirect that follows a form post, so the page allows the client's origin.
        using var client = factory.CreateClient();
        var admin = IdentityProviderFlow.AuthorizeParameters(IdentityProviderFlow.AdminClientId, IdentityProviderFlow.AdminRedirectUri, Pkce.Create());

        using var adminPage = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(admin), TestContext.Current.CancellationToken);
        using var mobilePage = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);

        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self' http://localhost:3000; frame-ancestors 'none'",
            Assert.Single(adminPage.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self' azwah:; frame-ancestors 'none'",
            Assert.Single(mobilePage.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task The_persona_page_html_encodes_every_reflected_value()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["state"] = "\"><script>alert(1)</script>";
        parameters["nonce"] = "<img src=x onerror=alert(2)>";

        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("value=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(2)&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Personas))]
    public async Task Choosing_a_persona_redirects_with_a_code_the_state_and_iss(string persona)
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        var redirect = await IdentityProviderFlow.ChooseAsync(client, parameters, persona, TestContext.Current.CancellationToken);

        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.False(string.IsNullOrWhiteSpace(redirect["code"]));
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        Assert.Null(redirect["error"]);
    }

    [Fact]
    public async Task Cancel_redirects_with_access_denied_the_state_and_iss()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        using var response = await client.PostAsync(
            "/authorize", IdentityProviderFlow.Form(parameters, ("cancel", "cancel")), TestContext.Current.CancellationToken);

        var redirect = AssertRedirect(response, IdentityProviderFlow.MobileRedirectUri);
        Assert.Equal("access_denied", redirect["error"]);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Null(redirect["code"]);
    }

    [Fact]
    public async Task Posting_the_form_validates_the_client_and_redirect_uri_again()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["redirect_uri"] = "https://attacker.example/callback";

        using var response = await client.PostAsync(
            "/authorize", IdentityProviderFlow.Form(parameters, ("persona", "researcher")), TestContext.Current.CancellationToken);

        await AssertErrorPageWithoutRedirectAsync(response);
    }

    [Fact]
    public async Task Posting_the_form_validates_the_rest_of_the_request_again()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["code_challenge_method"] = "plain";

        using var response = await client.PostAsync(
            "/authorize", IdentityProviderFlow.Form(parameters, ("persona", "researcher")), TestContext.Current.CancellationToken);

        var redirect = AssertRedirect(response, IdentityProviderFlow.MobileRedirectUri);
        Assert.Equal("invalid_request", redirect["error"]);
        Assert.Null(redirect["code"]);
    }

    [Fact]
    public async Task An_unknown_persona_redirects_with_invalid_request()
    {
        using var client = factory.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        using var response = await client.PostAsync(
            "/authorize", IdentityProviderFlow.Form(parameters, ("persona", "superuser")), TestContext.Current.CancellationToken);

        var redirect = AssertRedirect(response, IdentityProviderFlow.MobileRedirectUri);
        Assert.Equal("invalid_request", redirect["error"]);
        Assert.Null(redirect["code"]);
    }
}
