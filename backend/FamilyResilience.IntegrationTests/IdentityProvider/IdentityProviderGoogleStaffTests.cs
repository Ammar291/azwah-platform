using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// Google staff sign-in for the demonstration stack (D-149): in Development only, a Google sign-in for a client that opts
/// in with <c>GoogleStaffSignIn</c> gets the subject <c>google-staff|{sub}</c> and <c>IdentityProvider:Google:StaffRoles</c>
/// instead of <c>google|{sub}</c> and <c>participant</c>. Against the <see cref="FakeGoogle"/> stub.
/// </summary>
public sealed class IdentityProviderGoogleStaffTests
{
    private const string StaffSubject = "google-staff|109876543210987654321";
    private const string FamilySubject = "google|109876543210987654321";

    private static readonly string[] AllStaffRoles = ["system_administrator", "program_administrator", "researcher", "support_coordinator"];

    private static IdentityProviderFactory StaffProvider(params string[] roles) => new()
    {
        GoogleEnabled = true,
        GoogleStaffRoles = roles.Length == 0 ? AllStaffRoles : roles,
        AdminGoogleStaffSignIn = true,
    };

    private static string[] Roles(JsonElement token) => [.. token.GetProperty("role").EnumerateArray().Select(role => role.GetString()!)];

    private static (string Subject, string[] Roles) Identity(JsonElement tokens, string name)
    {
        var payload = TestJwt.Payload(tokens.GetProperty(name).GetString()!);
        return (payload.GetProperty("sub").GetString()!, payload.TryGetProperty("role", out _) ? Roles(payload) : []);
    }

    private static void AssertFamilyAccount(JsonElement tokens)
    {
        var (subject, roles) = Identity(tokens, "access_token");
        Assert.Equal(FamilySubject, subject);
        Assert.Equal(new[] { "participant" }, roles);
    }

    private static string StartupFailure(IdentityProviderFactory factory, Dictionary<string, string?>? settings = null)
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings ?? [])));
        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        return Describe(exception);
    }

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";

    private static async Task<string> PersonaPageAsync(HttpClient client, string clientId)
    {
        var redirectUri = clientId == IdentityProviderFlow.AdminClientId ? IdentityProviderFlow.AdminRedirectUri : IdentityProviderFlow.MobileRedirectUri;
        using var page = await client.GetAsync(
            IdentityProviderFlow.AuthorizeUrl(IdentityProviderFlow.AuthorizeParameters(clientId, redirectUri, Pkce.Create())),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        return await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("researcher")]
    [InlineData("system_administrator,program_administrator,researcher,support_coordinator")]
    public async Task A_google_sign_in_for_the_staff_client_gets_a_staff_subject_and_the_configured_roles(string configured)
    {
        var roles = configured.Split(',');
        await using var provider = StaffProvider(roles);
        using var client = provider.CreateClient();

        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        var access = Identity(tokens, "access_token");
        Assert.Equal(StaffSubject, access.Subject);
        Assert.Equal(roles.Order(StringComparer.Ordinal), access.Roles.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("participant", access.Roles);
        var id = TestJwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal(StaffSubject, id.GetProperty("sub").GetString());
        Assert.Equal("google", id.GetProperty("azwah_profile").GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_refreshed_staff_token_keeps_the_staff_subject_and_roles()
    {
        await using var provider = StaffProvider();
        using var client = provider.CreateClient();
        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        using var refresh = await IdentityProviderFlow.RefreshAsync(
            client, ClientCredentials.AdminBasic(), tokens.GetProperty("refresh_token").GetString()!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await IdentityProviderFlow.ReadJsonAsync(refresh, TestContext.Current.CancellationToken);
        var access = Identity(refreshed, "access_token");
        Assert.Equal(StaffSubject, access.Subject);
        Assert.Equal(AllStaffRoles.Order(StringComparer.Ordinal), access.Roles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_phone_google_sign_in_is_unchanged_while_staff_sign_in_is_on()
    {
        await using var provider = StaffProvider();
        using var client = provider.CreateClient();

        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken);

        AssertFamilyAccount(tokens);
        Assert.Equal(FamilySubject, Identity(tokens, "id_token").Subject);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task With_staff_sign_in_off_every_client_gets_the_family_subject_and_participant(bool staffRoles, bool adminOptsIn)
    {
        await using var provider = new IdentityProviderFactory
        {
            GoogleEnabled = true,
            GoogleStaffRoles = staffRoles ? AllStaffRoles : [],
            AdminGoogleStaffSignIn = adminOptsIn,
        };
        using var client = provider.CreateClient();

        var admin = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());
        var phone = await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken);

        AssertFamilyAccount(admin);
        AssertFamilyAccount(phone);
    }

    [Fact]
    public async Task Persona_sign_in_for_the_staff_client_is_unchanged()
    {
        await using var provider = StaffProvider("researcher");
        using var client = provider.CreateClient();

        var signedIn = await IdentityProviderFlow.SignInAsync(
            client, "system_administrator", TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        var access = TestJwt.Payload(signedIn.AccessToken);
        Assert.Equal("dev-system-administrator", access.GetProperty("sub").GetString());
        Assert.Equal(new[] { "system_administrator" }, Roles(access));
    }

    [Fact]
    public void The_assignable_staff_roles_are_the_platforms_roles_without_participant()
    {
        var platformStaffRoles = FamilyResilience.Application.Identity.Roles.All
            .Where(role => role != FamilyResilience.Application.Identity.Roles.Participant);

        Assert.Equal(
            platformStaffRoles.Order(StringComparer.Ordinal),
            FamilyResilience.IdentityProvider.GoogleOptions.AssignableStaffRoles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_deployed_provider_refuses_to_start_with_staff_roles()
    {
        var failure = StartupFailure(
            new IdentityProviderFactory
            {
                Deployed = true, UaePassEnabled = true, GoogleEnabled = true, SigningKeyPem = IdentityProviderFactory.TestRsaPem(),
                GoogleStaffRoles = ["researcher"],
            });

        Assert.Contains("IdentityProvider:Google:StaffRoles must be empty outside Development", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deployed_provider_refuses_to_start_with_a_client_that_opts_in_to_google_staff_sign_in()
    {
        var failure = StartupFailure(
            new IdentityProviderFactory
            {
                Deployed = true, UaePassEnabled = true, GoogleEnabled = true, SigningKeyPem = IdentityProviderFactory.TestRsaPem(),
                AdminGoogleStaffSignIn = true,
            });

        Assert.Contains("IdentityProvider:Clients:azwah-admin:GoogleStaffSignIn must be false outside Development", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("participant", "IdentityProvider:Google:StaffRoles:1 must not be participant")]
    [InlineData("administrator", "IdentityProvider:Google:StaffRoles:1 must be one of")]
    [InlineData("Researcher", "IdentityProvider:Google:StaffRoles:1 must be one of")]
    [InlineData(" ", "IdentityProvider:Google:StaffRoles:1 must be one of")]
    public void Development_refuses_a_staff_role_the_platform_does_not_know_and_participant(string role, string expected)
    {
        var failure = StartupFailure(StaffProvider("researcher", role));

        Assert.Contains(expected, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_refuses_google_staff_sign_in_for_a_public_client()
    {
        var failure = StartupFailure(
            StaffProvider(),
            new Dictionary<string, string?> { ["IdentityProvider:Clients:azwah-mobile:GoogleStaffSignIn"] = "true" });

        Assert.Contains("IdentityProvider:Clients:azwah-mobile:GoogleStaffSignIn", failure, StringComparison.Ordinal);
        Assert.Contains("confidential", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_persona_page_offers_google_as_staff_to_the_staff_client_only()
    {
        await using var provider = StaffProvider("researcher", "program_administrator");
        using var client = provider.CreateClient();

        var admin = await PersonaPageAsync(client, IdentityProviderFlow.AdminClientId);
        var phone = await PersonaPageAsync(client, IdentityProviderFlow.MobileClientId);

        var link = Regex.Match(admin, "<a class=\"upstream\" href=\"([^\"]+)\">Continue with Google");
        Assert.True(link.Success);
        Assert.Contains("idp_hint=google", WebUtility.HtmlDecode(link.Groups[1].Value), StringComparison.Ordinal);
        Assert.Contains("client_id=azwah-admin", WebUtility.HtmlDecode(link.Groups[1].Value), StringComparison.Ordinal);
        Assert.Contains("signed in as staff with roles: researcher, program_administrator", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("signed in as a participant", admin, StringComparison.Ordinal);

        Assert.Contains("Continue with Google", phone, StringComparison.Ordinal);
        Assert.Contains("signed in as a participant", phone, StringComparison.Ordinal);
        Assert.DoesNotContain("as staff", phone, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_staff_sign_in_off_the_persona_page_for_the_staff_client_is_unchanged()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true, AdminGoogleStaffSignIn = true };
        using var client = provider.CreateClient();

        var admin = await PersonaPageAsync(client, IdentityProviderFlow.AdminClientId);

        Assert.Contains("Continue with Google", admin, StringComparison.Ordinal);
        Assert.Contains("signed in as a participant", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("as staff", admin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_start_up_warns_that_google_accounts_sign_in_to_the_staff_client_as_staff()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = StaffProvider("researcher", "system_administrator");
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();

        var warning = Assert.Single(logs.Entries, entry => entry.EventId == 3015);

        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("azwah-admin", warning.Message, StringComparison.Ordinal);
        Assert.Contains("researcher, system_administrator", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IdentityProviderFactory.GoogleClientSecret, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_up_says_nothing_about_staff_sign_in_while_it_is_off()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true, AdminGoogleStaffSignIn = true };
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();

        Assert.DoesNotContain(logs.Entries, entry => entry.EventId == 3015);
    }

    [Fact]
    public async Task The_sign_in_log_names_the_staff_persona_code_but_nothing_google_sent()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = StaffProvider();
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = host.CreateClient();

        await IdentityProviderFlow.SignInWithGoogleAsync(client, TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());

        Assert.Contains(logs.Entries, entry => entry.EventId == 3002 && entry.Message.Contains("google_staff", StringComparison.Ordinal)
            && entry.Message.Contains("azwah-admin", StringComparison.Ordinal));
        var everything = string.Join('\n', logs.Entries.Select(entry => entry.Message));
        Assert.DoesNotContain("109876543210987654321", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("test.google", everything, StringComparison.Ordinal);
    }
}
