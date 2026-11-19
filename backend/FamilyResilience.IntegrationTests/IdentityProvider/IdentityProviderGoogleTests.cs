using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>The optional Google sign-in (D-147), against the <see cref="FakeGoogle"/> stub.</summary>
public sealed class IdentityProviderGoogleTests
{
    private const string Callback = "http://localhost:5090/google/callback";
    private const string ExpectedSubject = "google|109876543210987654321";

    private static readonly string[] PersonalClaims =
        ["name", "given_name", "family_name", "gender", "email", "phone_number", "birthdate", "azwah_profile", "picture"];

    private static async Task<AuthorizationResponse> StartAsync(HttpClient client, Dictionary<string, string?> parameters)
    {
        parameters["idp_hint"] = "google";
        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return AuthorizationResponse.From(response.Headers.Location!);
    }

    private static async Task<HttpResponseMessage> CallbackAsync(HttpClient client, params (string Key, string Value)[] query) =>
        await client.GetAsync(
            QueryHelpers.AddQueryString("/google/callback", query.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))),
            TestContext.Current.CancellationToken);

    private static async Task<(AuthorizationResponse Upstream, AuthorizationResponse Redirect, Dictionary<string, string?> Parameters, Pkce Pkce)> SignInAsync(HttpClient client)
    {
        var pkce = Pkce.Create();
        var parameters = IdentityProviderFlow.MobileParameters(pkce);
        var upstream = await StartAsync(client, parameters);
        using var callback = await CallbackAsync(client, ("code", "stub-google-code"), ("state", upstream.Require("state")));
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return (upstream, AuthorizationResponse.From(callback.Headers.Location!), parameters, pkce);
    }

    private static async Task<JsonElement> RedeemAsync(HttpClient client, AuthorizationResponse redirect, Pkce pkce)
    {
        using var response = await IdentityProviderFlow.ExchangeCodeAsync(
            client, ClientCredentials.Mobile, redirect.Require("code"), IdentityProviderFlow.MobileRedirectUri, pkce.Verifier,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
    }

    private static void AssertTemporarilyUnavailable(AuthorizationResponse redirect, Dictionary<string, string?> parameters)
    {
        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("temporarily_unavailable", redirect["error"]);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        Assert.Null(redirect["code"]);
    }

    private static async Task AssertRefusedPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("unknown, expired or already finished", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("ar", "ar")]
    [InlineData("en", "en")]
    public async Task The_hint_sends_the_browser_to_google_with_the_callback_pkce_the_account_chooser_and_a_fresh_state(string? uiLocales, string expectedLanguage)
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["ui_locales"] = uiLocales;

        var upstream = await StartAsync(client, parameters);

        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", upstream.Target);
        Assert.Equal("code", upstream["response_type"]);
        Assert.Equal(IdentityProviderFactory.GoogleClientId, upstream["client_id"]);
        Assert.Equal(Callback, upstream["redirect_uri"]);
        Assert.Equal("openid email profile", upstream["scope"]);
        Assert.Equal("select_account", upstream["prompt"]);
        Assert.Equal(expectedLanguage, upstream["hl"]);
        Assert.Equal("S256", upstream["code_challenge_method"]);
        Assert.Equal(43, upstream.Require("code_challenge").Length);
        Assert.NotEqual(parameters["code_challenge"], upstream["code_challenge"]);
        Assert.False(string.IsNullOrWhiteSpace(upstream["state"]));
        Assert.NotEqual(parameters["state"], upstream["state"]);
        Assert.Null(upstream["client_secret"]);
        Assert.Null(upstream["nonce"]);
        Assert.Empty(provider.Google.Requests);
    }

    [Fact]
    public async Task With_google_disabled_the_hint_redirects_back_with_temporarily_unavailable()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        var redirect = await StartAsync(client, parameters);

        AssertTemporarilyUnavailable(redirect, parameters);
        Assert.Contains("Google sign-in is not enabled", redirect["error_description"], StringComparison.Ordinal);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Fact]
    public async Task An_invalid_request_with_the_hint_is_refused_before_anything_goes_to_google()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["code_challenge_method"] = "plain";

        var redirect = await StartAsync(client, parameters);

        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("invalid_request", redirect["error"]);
    }

    [Fact]
    public async Task The_callback_exchanges_the_code_with_the_verifier_then_the_client_redeems_tokens_with_a_masked_profile_in_the_id_token_only()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();

        var (upstream, redirect, parameters, pkce) = await SignInAsync(client);

        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        Assert.Null(redirect["error"]);

        var exchange = provider.Google.Requests[0];
        Assert.Equal((HttpMethod.Post, "https://oauth2.googleapis.com/token"), (exchange.Method, exchange.Uri.ToString()));
        Assert.Null(exchange.Authorization);
        var form = QueryHelpers.ParseQuery(exchange.Body);
        Assert.Equal(("authorization_code", "stub-google-code", Callback), (form["grant_type"].ToString(), form["code"].ToString(), form["redirect_uri"].ToString()));
        Assert.Equal((IdentityProviderFactory.GoogleClientId, IdentityProviderFactory.GoogleClientSecret), (form["client_id"].ToString(), form["client_secret"].ToString()));
        var verifier = form["code_verifier"].ToString();
        Assert.Equal(upstream["code_challenge"], IdentityProviderFlow.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
        var userInfo = provider.Google.Requests[1];
        Assert.Equal((HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo"), (userInfo.Method, userInfo.Uri.ToString()));
        Assert.Equal($"Bearer {FakeGoogle.AccessToken}", userInfo.Authorization);

        var tokens = await RedeemAsync(client, redirect, pkce);

        var access = TestJwt.Payload(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(ExpectedSubject, access.GetProperty("sub").GetString());
        Assert.Equal(new[] { "participant" }, access.GetProperty("role").EnumerateArray().Select(role => role.GetString()!));
        Assert.DoesNotContain(TestJwt.Names(access), name => PersonalClaims.Contains(name));

        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal(ExpectedSubject, id.GetProperty("sub").GetString());
        Assert.Equal(parameters["nonce"], id.GetProperty("nonce").GetString());
        Assert.Equal(
            ("Test Google Person", "Test", "Person"),
            (id.GetProperty("name").GetString(), id.GetProperty("given_name").GetString(), id.GetProperty("family_name").GetString()));
        Assert.False(id.TryGetProperty("gender", out _));
        Assert.False(id.TryGetProperty("picture", out _));
        Assert.Equal(
            new Dictionary<string, string> { ["source"] = "google", ["email_masked"] = "t•••@example.com" },
            id.GetProperty("azwah_profile").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!));

        foreach (var jwt in new[] { tokens.GetProperty("access_token").GetString()!, tokens.GetProperty("id_token").GetString()! })
        {
            Assert.DoesNotContain("test.google@", TestJwt.Payload(jwt).GetRawText(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("""{"sub":"123","email":"someone@example.com","email_verified":false}""")]
    [InlineData("""{"sub":"123","email":"someone@example.com"}""")]
    public async Task An_unverified_email_address_is_left_out_and_only_the_sub_is_required(string userInfo)
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        provider.Google.UserInfoBody = userInfo;
        using var client = provider.CreateClient();

        var (_, redirect, _, pkce) = await SignInAsync(client);
        var tokens = await RedeemAsync(client, redirect, pkce);

        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal("google|123", id.GetProperty("sub").GetString());
        Assert.False(id.TryGetProperty("name", out _));
        Assert.Equal(new[] { "source" }, TestJwt.Names(id.GetProperty("azwah_profile")));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("interaction_required")]
    public async Task An_error_from_google_redirects_back_with_access_denied_and_calls_nothing(string upstreamError)
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);

        using var callback = await CallbackAsync(client, ("error", upstreamError), ("state", upstream.Require("state")));

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var redirect = AuthorizationResponse.From(callback.Headers.Location!);
        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("access_denied", redirect["error"]);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Empty(provider.Google.Requests);
    }

    [Theory]
    [InlineData("token_rejected")]
    [InlineData("token_not_json")]
    [InlineData("token_without_access_token")]
    [InlineData("userinfo_rejected")]
    [InlineData("userinfo_without_sub")]
    [InlineData("userinfo_with_unusable_sub")]
    [InlineData("unreachable")]
    public async Task A_failure_talking_to_google_redirects_back_with_temporarily_unavailable(string failure)
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        Break(provider.Google, failure);
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);

        using var callback = await CallbackAsync(client, ("code", "stub-google-code"), ("state", upstream.Require("state")));

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        AssertTemporarilyUnavailable(AuthorizationResponse.From(callback.Headers.Location!), parameters);
    }

    private static void Break(FakeGoogle fake, string failure)
    {
        switch (failure)
        {
            case "token_rejected":
                fake.TokenStatus = HttpStatusCode.BadRequest;
                fake.TokenBody = """{"error":"invalid_grant"}""";
                break;
            case "token_not_json":
                fake.TokenBody = "<html>error</html>";
                break;
            case "token_without_access_token":
                fake.TokenBody = """{"token_type":"Bearer"}""";
                break;
            case "userinfo_rejected":
                fake.UserInfoStatus = HttpStatusCode.Unauthorized;
                break;
            case "userinfo_without_sub":
                fake.UserInfoBody = """{"name":"No Subject"}""";
                break;
            case "userinfo_with_unusable_sub":
                fake.UserInfoBody = """{"sub":"has spaces | and pipes"}""";
                break;
            default:
                fake.Unreachable = true;
                break;
        }
    }

    [Fact]
    public async Task A_reused_state_is_refused_on_a_page_without_a_redirect()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();
        var upstream = await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()));
        using (var first = await CallbackAsync(client, ("code", "stub-google-code"), ("state", upstream.Require("state"))))
        {
            Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        }

        using var second = await CallbackAsync(client, ("code", "stub-google-code"), ("state", upstream.Require("state")));

        await AssertRefusedPageAsync(second);
        Assert.Equal(2, provider.Google.Requests.Count);
    }

    [Fact]
    public async Task A_state_older_than_ten_minutes_is_refused()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var client = provider.CreateClient();
        var upstream = await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()));
        provider.Clock.Advance(TimeSpan.FromMinutes(10));

        using var response = await CallbackAsync(client, ("code", "stub-google-code"), ("state", upstream.Require("state")));

        await AssertRefusedPageAsync(response);
        Assert.Empty(provider.Google.Requests);
    }

    [Fact]
    public async Task A_state_issued_for_uae_pass_is_refused_at_the_google_callback_and_the_other_way_round()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true, UaePassEnabled = true };
        using var client = provider.CreateClient();

        var uaePassParameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        uaePassParameters["idp_hint"] = "uae_pass";
        using var uaePassStart = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(uaePassParameters), TestContext.Current.CancellationToken);
        var uaePassState = AuthorizationResponse.From(uaePassStart.Headers.Location!).Require("state");
        using var atGoogle = await CallbackAsync(client, ("code", "stub-google-code"), ("state", uaePassState));
        await AssertRefusedPageAsync(atGoogle);

        var googleState = (await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()))).Require("state");
        using var atUaePass = await client.GetAsync(
            QueryHelpers.AddQueryString("/uaepass/callback", new Dictionary<string, string?> { ["code"] = "stub-upstream-code", ["state"] = googleState }),
            TestContext.Current.CancellationToken);
        await AssertRefusedPageAsync(atUaePass);

        Assert.Empty(provider.Google.Requests);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Fact]
    public async Task The_persona_page_offers_google_next_to_uae_pass_as_links()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true, UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var page = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var links = System.Text.RegularExpressions.Regex.Matches(html, "<a class=\"upstream\" href=\"([^\"]+)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .ToList();
        Assert.Equal(2, links.Count);
        Assert.Contains("idp_hint=uae_pass", links[0], StringComparison.Ordinal);
        Assert.Contains("idp_hint=google", links[1], StringComparison.Ordinal);
        Assert.Contains("Continue with UAE PASS (staging)", html, StringComparison.Ordinal);
        Assert.Contains("Continue with Google", html, StringComparison.Ordinal);
        Assert.Contains("Or choose a simulated persona:", html, StringComparison.Ordinal);

        using var chosen = await client.GetAsync(links[1], TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, chosen.StatusCode);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", AuthorizationResponse.From(chosen.Headers.Location!).Target);
    }

    [Fact]
    public async Task The_persona_page_does_not_offer_google_when_disabled()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var page = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Continue with Google", html, StringComparison.Ordinal);
        Assert.DoesNotContain("idp_hint=google", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, new string[0])]
    [InlineData(true, false, new[] { "uae_pass" })]
    [InlineData(false, true, new[] { "google" })]
    [InlineData(true, true, new[] { "uae_pass", "google" })]
    public async Task Discovery_lists_the_configured_upstream_sign_ins(bool uaePass, bool google, string[] expected)
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = uaePass, GoogleEnabled = google };
        using var client = provider.CreateClient();

        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var metadata = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);

        Assert.Equal(expected, metadata.GetProperty("azwah_identity_providers").EnumerateArray().Select(hint => hint.GetString()!));
        var claims = metadata.GetProperty("claims_supported").EnumerateArray().Select(claim => claim.GetString()!).ToList();
        Assert.Equal(uaePass || google, claims.Contains("azwah_profile"));
    }

    [Fact]
    public async Task A_deployed_provider_sends_the_google_hint_to_google_and_every_other_request_to_uae_pass()
    {
        await using var provider = new IdentityProviderFactory
        {
            Deployed = true, UaePassEnabled = true, GoogleEnabled = true, SigningKeyPem = IdentityProviderFactory.TestRsaPem(),
        };
        using var client = provider.CreateClient();

        var google = await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()));
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", google.Target);
        Assert.Equal($"{IdentityProviderFactory.DeployedIssuer}/google/callback", google["redirect_uri"]);

        using var plain = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/authorize", AuthorizationResponse.From(plain.Headers.Location!).Target);

        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken);
        var access = TestJwt.Payload(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(ExpectedSubject, access.GetProperty("sub").GetString());
        Assert.False(access.TryGetProperty("azwah_development_only", out _));
    }

    [Fact]
    public async Task The_log_names_the_client_and_a_fixed_reason_but_nothing_google_sent()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();

        var (first, _, _, _) = await SignInAsync(client);
        provider.Google.UserInfoStatus = HttpStatusCode.Unauthorized;
        var upstream = await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()));
        using (await CallbackAsync(client, ("code", "second-google-code"), ("state", upstream.Require("state"))))
        {
        }

        Assert.Contains(logs.Entries, entry => entry.EventId == 3012);
        Assert.Contains(logs.Entries, entry => entry.EventId == 3002 && entry.Message.Contains("google", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.EventId == 3013 && entry.Message.Contains("userinfo_rejected", StringComparison.Ordinal));
        var everything = string.Join('\n', logs.Entries.Select(entry => entry.Message));
        foreach (var secret in new[]
                 {
                     "109876543210987654321", "stub-google-code", "second-google-code", FakeGoogle.AccessToken,
                     IdentityProviderFactory.GoogleClientSecret, "test.google", "Test Google Person", "/v1/userinfo",
                     first.Require("state"), upstream.Require("state"),
                 })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }
}
