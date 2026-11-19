using System.Net;
using System.Text;
using System.Text.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>The optional UAE PASS staging sign-in (D-96), against the <see cref="FakeUaePass"/> stub.</summary>
public sealed class IdentityProviderUaePassTests
{
    private const string Callback = "http://localhost:5090/uaepass/callback";
    private const string ExpectedSubject = "uaepass-stg|11111111-2222-3333-4444-555555555555";

    private static readonly string[] PersonalClaims =
        ["name", "given_name", "family_name", "gender", "email", "phone_number", "birthdate", "azwah_profile", "idn", "mobile"];

    private static async Task<AuthorizationResponse> StartAsync(HttpClient client, Dictionary<string, string?> parameters)
    {
        parameters["idp_hint"] = "uae_pass";
        using var response = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return AuthorizationResponse.From(response.Headers.Location!);
    }

    private static async Task<HttpResponseMessage> CallbackAsync(HttpClient client, params (string Key, string Value)[] query) =>
        await client.GetAsync(
            QueryHelpers.AddQueryString("/uaepass/callback", query.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))),
            TestContext.Current.CancellationToken);

    /// <summary>The whole round trip: authorize with the hint, UAE PASS answers with a code, the provider redirects back.</summary>
    private static async Task<(AuthorizationResponse Redirect, Dictionary<string, string?> Parameters, Pkce Pkce)> SignInAsync(HttpClient client)
    {
        var pkce = Pkce.Create();
        var parameters = IdentityProviderFlow.MobileParameters(pkce);
        var upstream = await StartAsync(client, parameters);
        using var callback = await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", upstream.Require("state")));
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        return (AuthorizationResponse.From(callback.Headers.Location!), parameters, pkce);
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

    [Theory]
    [InlineData(null, "en")]
    [InlineData("ar", "ar")]
    [InlineData("ar-AE en", "ar")]
    [InlineData("en", "en")]
    [InlineData("fr", "en")]
    public async Task The_hint_sends_the_browser_to_uae_pass_staging_with_the_callback_and_a_fresh_state(string? uiLocales, string expectedLocale)
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["ui_locales"] = uiLocales;

        var upstream = await StartAsync(client, parameters);

        Assert.Equal("https://stg-id.uaepass.ae/idshub/authorize", upstream.Target);
        Assert.Equal("code", upstream["response_type"]);
        Assert.Equal(IdentityProviderFactory.UaePassClientId, upstream["client_id"]);
        Assert.Equal(Callback, upstream["redirect_uri"]);
        Assert.Equal("urn:uae:digitalid:profile:general", upstream["scope"]);
        Assert.Equal("urn:safelayer:tws:policies:authentication:level:low", upstream["acr_values"]);
        Assert.Equal(expectedLocale, upstream["ui_locales"]);
        Assert.False(string.IsNullOrWhiteSpace(upstream["state"]));
        Assert.NotEqual(parameters["state"], upstream["state"]);
        Assert.Null(upstream["nonce"]);
        Assert.Null(upstream["code_challenge"]);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Fact]
    public async Task With_uae_pass_disabled_the_hint_redirects_back_with_temporarily_unavailable()
    {
        await using var provider = new IdentityProviderFactory();
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());

        var redirect = await StartAsync(client, parameters);

        AssertTemporarilyUnavailable(redirect, parameters);
        Assert.Contains("not enabled", redirect["error_description"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_request_with_the_hint_is_refused_before_anything_goes_to_uae_pass()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["code_challenge_method"] = "plain";

        var redirect = await StartAsync(client, parameters);

        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("invalid_request", redirect["error"]);
    }

    [Fact]
    public async Task The_callback_exchanges_the_code_then_the_client_redeems_tokens_with_a_masked_profile_in_the_id_token_only()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        var (redirect, parameters, pkce) = await SignInAsync(client);

        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        Assert.Null(redirect["error"]);

        var exchange = provider.UaePass.Requests[0];
        Assert.Equal((HttpMethod.Post, "https://stg-id.uaepass.ae/idshub/token"), (exchange.Method, exchange.Uri.ToString()));
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{IdentityProviderFactory.UaePassClientId}:{IdentityProviderFactory.UaePassClientSecret}"));
        Assert.Equal($"Basic {basic}", exchange.Authorization);
        var form = QueryHelpers.ParseQuery(exchange.Body);
        Assert.Equal(("authorization_code", "stub-upstream-code", Callback), (form["grant_type"].ToString(), form["code"].ToString(), form["redirect_uri"].ToString()));
        var userInfo = provider.UaePass.Requests[1];
        Assert.Equal((HttpMethod.Get, "https://stg-id.uaepass.ae/idshub/userinfo"), (userInfo.Method, userInfo.Uri.ToString()));
        Assert.Equal($"Bearer {FakeUaePass.AccessToken}", userInfo.Authorization);

        var tokens = await RedeemAsync(client, redirect, pkce);

        var access = TestJwt.Payload(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(ExpectedSubject, access.GetProperty("sub").GetString());
        Assert.Equal(new[] { "participant" }, access.GetProperty("role").EnumerateArray().Select(role => role.GetString()!));
        Assert.True(access.GetProperty("azwah_development_only").GetBoolean());
        Assert.DoesNotContain(TestJwt.Names(access), name => PersonalClaims.Contains(name));

        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal(ExpectedSubject, id.GetProperty("sub").GetString());
        Assert.Equal(parameters["nonce"], id.GetProperty("nonce").GetString());
        Assert.True(id.GetProperty("azwah_development_only").GetBoolean());
        Assert.Equal(
            ("Test Staging Person", "Test", "Person", "male"),
            (id.GetProperty("name").GetString(), id.GetProperty("given_name").GetString(), id.GetProperty("family_name").GetString(),
                id.GetProperty("gender").GetString()));
        Assert.False(id.TryGetProperty("birthdate", out _));
        var profile = id.GetProperty("azwah_profile");
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["source"] = "uae_pass_staging",
                ["name_ar"] = "تجربة شخص",
                ["given_name_ar"] = "تجربة",
                ["family_name_ar"] = "شخص",
                ["nationality_en"] = "ARE",
                ["nationality_ar"] = "الإمارات",
                ["user_type"] = "SOP3",
                ["emirates_id_masked"] = "784-••••-•••••42-1",
                ["mobile_masked"] = "+971•••••••42",
                ["email_masked"] = "t•••@example.ae",
            },
            profile.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!));

        // The full Emirates ID, phone number and e-mail address appear in no token.
        foreach (var jwt in new[] { tokens.GetProperty("access_token").GetString()!, tokens.GetProperty("id_token").GetString()! })
        {
            var payload = TestJwt.Payload(jwt).GetRawText();
            Assert.DoesNotContain("784199912345421", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("971500000042", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("test.person@", payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Absent_profile_fields_are_omitted_and_only_the_uuid_is_required()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        provider.UaePass.UserInfoBody = """{"sub":"visitor","uuid":"aaaa-bbbb","userType":"SOP1","fullnameEN":"Visiting Tester","idn":12}""";
        using var client = provider.CreateClient();

        var (redirect, _, pkce) = await SignInAsync(client);
        var tokens = await RedeemAsync(client, redirect, pkce);

        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal("uaepass-stg|aaaa-bbbb", id.GetProperty("sub").GetString());
        Assert.Equal("Visiting Tester", id.GetProperty("name").GetString());
        Assert.False(id.TryGetProperty("gender", out _));
        Assert.Equal(new[] { "source", "user_type" }, TestJwt.Names(id.GetProperty("azwah_profile")));
    }

    [Fact]
    public async Task A_refresh_returns_an_id_token_that_keeps_the_profile_without_a_nonce()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var (redirect, _, pkce) = await SignInAsync(client);
        var tokens = await RedeemAsync(client, redirect, pkce);
        var original = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        provider.Clock.Advance(TimeSpan.FromMinutes(5));

        using var response = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.Mobile, tokens.GetProperty("refresh_token").GetString()!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        var id = TestJwt.Payload(refreshed.GetProperty("id_token").GetString()!);
        Assert.Equal(ExpectedSubject, id.GetProperty("sub").GetString());
        Assert.False(id.TryGetProperty("nonce", out _));
        Assert.Equal(original.GetProperty("auth_time").GetInt64(), id.GetProperty("auth_time").GetInt64());
        Assert.True(id.GetProperty("iat").GetInt64() > original.GetProperty("iat").GetInt64());
        Assert.Equal(original.GetProperty("azwah_profile").GetRawText(), id.GetProperty("azwah_profile").GetRawText());
        Assert.Equal("Test Staging Person", id.GetProperty("name").GetString());
        Assert.DoesNotContain(TestJwt.Names(TestJwt.Payload(refreshed.GetProperty("access_token").GetString()!)), name => PersonalClaims.Contains(name));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("login_required")]
    public async Task An_error_from_uae_pass_redirects_back_with_access_denied_and_calls_nothing(string upstreamError)
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);

        using var callback = await CallbackAsync(
            client, ("error", upstreamError), ("error_description", "User cancelled the login"), ("state", upstream.Require("state")));

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var redirect = AuthorizationResponse.From(callback.Headers.Location!);
        Assert.Equal(IdentityProviderFlow.MobileRedirectUri, redirect.Target);
        Assert.Equal("access_denied", redirect["error"]);
        Assert.Equal(parameters["state"], redirect["state"]);
        Assert.Equal(IdentityProviderFactory.Issuer, redirect["iss"]);
        Assert.DoesNotContain("User cancelled", redirect["error_description"], StringComparison.Ordinal);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Theory]
    [InlineData("token_rejected")]
    [InlineData("token_not_json")]
    [InlineData("token_without_access_token")]
    [InlineData("userinfo_rejected")]
    [InlineData("userinfo_not_json")]
    [InlineData("userinfo_without_uuid")]
    [InlineData("userinfo_with_unusable_uuid")]
    [InlineData("unreachable")]
    public async Task A_failure_talking_to_uae_pass_redirects_back_with_temporarily_unavailable(string failure)
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        Break(provider.UaePass, failure);
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);

        using var callback = await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", upstream.Require("state")));

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        AssertTemporarilyUnavailable(AuthorizationResponse.From(callback.Headers.Location!), parameters);
    }

    private static void Break(FakeUaePass fake, string failure)
    {
        switch (failure)
        {
            case "token_rejected":
                fake.TokenStatus = HttpStatusCode.BadRequest;
                fake.TokenBody = """{"error":"invalid_grant"}""";
                break;
            case "token_not_json":
                fake.TokenBody = "<html>maintenance</html>";
                break;
            case "token_without_access_token":
                fake.TokenBody = """{"token_type":"Bearer"}""";
                break;
            case "userinfo_rejected":
                fake.UserInfoStatus = HttpStatusCode.Unauthorized;
                break;
            case "userinfo_not_json":
                fake.UserInfoBody = "not json";
                break;
            case "userinfo_without_uuid":
                fake.UserInfoBody = """{"sub":"x","fullnameEN":"No Uuid"}""";
                break;
            case "userinfo_with_unusable_uuid":
                fake.UserInfoBody = """{"uuid":"has spaces | and pipes"}""";
                break;
            default:
                fake.Unreachable = true;
                break;
        }
    }

    [Fact]
    public async Task A_callback_without_a_code_redirects_back_with_temporarily_unavailable()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);

        using var callback = await CallbackAsync(client, ("state", upstream.Require("state")));

        AssertTemporarilyUnavailable(AuthorizationResponse.From(callback.Headers.Location!), parameters);
    }

    [Fact]
    public async Task A_reused_state_is_refused_on_a_page_without_a_redirect()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);
        using (var first = await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", upstream.Require("state"))))
        {
            Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        }

        using var second = await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", upstream.Require("state")));

        await AssertRefusedPageAsync(second);
        Assert.Equal(2, provider.UaePass.Requests.Count);
    }

    [Theory]
    [InlineData("unknown-state")]
    [InlineData(null)]
    public async Task An_unknown_or_missing_state_is_refused_on_a_page_without_a_redirect(string? state)
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();

        using var response = state is null
            ? await CallbackAsync(client, ("code", "stub-upstream-code"))
            : await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", state));

        await AssertRefusedPageAsync(response);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Fact]
    public async Task A_state_older_than_ten_minutes_is_refused()
    {
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var upstream = await StartAsync(client, IdentityProviderFlow.MobileParameters(Pkce.Create()));
        provider.Clock.Advance(TimeSpan.FromMinutes(10));

        using var response = await CallbackAsync(client, ("code", "stub-upstream-code"), ("state", upstream.Require("state")));

        await AssertRefusedPageAsync(response);
        Assert.Empty(provider.UaePass.Requests);
    }

    [Fact]
    public async Task The_persona_page_offers_uae_pass_when_enabled_as_a_link_that_no_form_policy_can_block()
    {
        // UAE PASS redirects from stg-id to stg-ids and browsers apply form-action to every redirect after a form
        // post, so a submit button was silently blocked in Chrome. A link is not a form submission.
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        using var client = provider.CreateClient();
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        parameters["ui_locales"] = "ar";

        using var page = await client.GetAsync(IdentityProviderFlow.AuthorizeUrl(parameters), TestContext.Current.CancellationToken);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var link = System.Text.RegularExpressions.Regex.Match(html, "<a class=\"upstream\" href=\"([^\"]+)\"");
        Assert.True(link.Success, "The UAE PASS choice is a link.");
        var href = WebUtility.HtmlDecode(link.Groups[1].Value);
        Assert.StartsWith("/authorize?", href, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"idp_hint\"", html, StringComparison.Ordinal);
        Assert.Contains("Continue with UAE PASS (staging)", html, StringComparison.Ordinal);
        Assert.Contains("name=\"ui_locales\" value=\"ar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self' azwah:; frame-ancestors 'none'",
            Assert.Single(page.Headers.GetValues("Content-Security-Policy")));

        using var chosen = await client.GetAsync(href, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Found, chosen.StatusCode);
        var upstream = AuthorizationResponse.From(chosen.Headers.Location!);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/authorize", upstream.Target);
        Assert.Equal("ar", upstream["ui_locales"]);
    }

    [Fact]
    public async Task The_persona_page_does_not_offer_uae_pass_when_disabled()
    {
        await using var provider = new IdentityProviderFactory();
        using var client = provider.CreateClient();

        using var page = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.MobileParameters(Pkce.Create())), TestContext.Current.CancellationToken);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("idp_hint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("uaepass.ae", Assert.Single(page.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discovery_lists_the_profile_claims_only_when_uae_pass_is_enabled()
    {
        await using var enabled = new IdentityProviderFactory { UaePassEnabled = true };
        await using var disabled = new IdentityProviderFactory();

        var on = await ClaimsSupportedAsync(enabled);
        var off = await ClaimsSupportedAsync(disabled);

        Assert.Contains("azwah_profile", on);
        Assert.Contains("name", on);
        Assert.DoesNotContain("azwah_profile", off);
        Assert.DoesNotContain("name", off);
    }

    [Fact]
    public async Task The_log_names_the_client_and_a_fixed_reason_but_nothing_uae_pass_sent()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = new IdentityProviderFactory { UaePassEnabled = true };
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();

        await SignInAsync(client);
        provider.UaePass.UserInfoStatus = HttpStatusCode.Unauthorized;
        var parameters = IdentityProviderFlow.MobileParameters(Pkce.Create());
        var upstream = await StartAsync(client, parameters);
        using (await CallbackAsync(client, ("code", "second-upstream-code"), ("state", upstream.Require("state"))))
        {
        }

        Assert.Contains(logs.Entries, entry => entry.EventId == 3007);
        Assert.Contains(logs.Entries, entry => entry.EventId == 3002 && entry.Message.Contains("uae_pass_staging", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.EventId == 3008 && entry.Message.Contains("userinfo_rejected", StringComparison.Ordinal));
        var everything = string.Join('\n', logs.Entries.Select(entry => entry.Message));
        foreach (var secret in new[]
                 {
                     "11111111-2222-3333-4444-555555555555", "stub-upstream-code", "second-upstream-code", FakeUaePass.AccessToken,
                     IdentityProviderFactory.UaePassClientSecret, "784199912345421", "971500000042", "test.person", "Test Staging Person",
                     "/idshub/token", "/idshub/userinfo", upstream.Require("state"),
                 })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    private static async Task<List<string>> ClaimsSupportedAsync(IdentityProviderFactory provider)
    {
        using var client = provider.CreateClient();
        using var response = await client.GetAsync("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var metadata = await IdentityProviderFlow.ReadJsonAsync(response, TestContext.Current.CancellationToken);
        return metadata.GetProperty("claims_supported").EnumerateArray().Select(claim => claim.GetString()!).ToList();
    }

    private static async Task AssertRefusedPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("unknown, expired or already finished", html, StringComparison.Ordinal);
    }
}
