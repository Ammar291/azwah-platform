using System.Text.Json;
using FamilyResilience.IdentityProvider;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>The UAE PASS staging settings are refused unless complete and pointed at staging only; profiles are masked.</summary>
public sealed class IdentityProviderUaePassSettingsTests
{
    private static string StartupFailure(Dictionary<string, string?> settings, bool deployed = false)
    {
        using var factory = (deployed ? IdentityProviderFactory.DeployedFactory() : new IdentityProviderFactory { UaePassEnabled = true }).WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings)));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        return Describe(exception);
    }

    private static ProviderMode DevelopmentMode() =>
        new(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development });

    private static string Describe(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Describe(exception.InnerException)}";

    [Theory]
    [InlineData("AuthorizationEndpoint", "https://id.uaepass.ae/idshub/authorize")]
    [InlineData("TokenEndpoint", "https://id.uaepass.ae/idshub/token")]
    [InlineData("UserInfoEndpoint", "https://id.uaepass.ae/idshub/userinfo")]
    [InlineData("TokenEndpoint", "http://stg-id.uaepass.ae/idshub/token")]
    [InlineData("TokenEndpoint", "https://stg-id.uaepass.ae.attacker.example/idshub/token")]
    [InlineData("TokenEndpoint", "https://stg-id.uaepass.ae:8443/idshub/token")]
    [InlineData("TokenEndpoint", "https://user@stg-id.uaepass.ae/idshub/token")]
    [InlineData("UserInfoEndpoint", "https://localhost/idshub/userinfo")]
    [InlineData("LogoutEndpoint", "https://id.uaepass.ae/idshub/logout")]
    [InlineData("AuthorizationEndpoint", "not a url")]
    public void It_refuses_any_uae_pass_endpoint_that_is_not_https_on_the_staging_host(string setting, string value)
    {
        var failure = StartupFailure(new() { [$"IdentityProvider:UaePass:{setting}"] = value });

        Assert.Contains($"IdentityProvider:UaePass:{setting}", failure, StringComparison.Ordinal);
        Assert.Contains("stg-id.uaepass.ae", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(IdentityProviderFactory.UaePassClientSecret, failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    public void A_deployed_provider_refuses_to_enable_uae_pass_without_client_credentials(string setting)
    {
        // In Development, empty credentials only leave UAE PASS off (IdentityProviderStartupTests).
        var failure = StartupFailure(new() { [$"IdentityProvider:UaePass:{setting}"] = string.Empty }, deployed: true);

        Assert.Contains($"IdentityProvider:UaePass:{setting} is required", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_settings_are_not_checked()
    {
        var options = new IdentityProviderOptions { UaePass = new UaePassOptions { Enabled = false, TokenEndpoint = "https://id.uaepass.ae/idshub/token" } };
        options.Clients["azwah-mobile"] = new IdentityProviderClientOptions { AllowExpoGoRedirects = true };

        Assert.True(new IdentityProviderOptionsValidator(DevelopmentMode()).Validate(null, options).Succeeded);
    }

    [Fact]
    public void The_defaults_point_at_uae_pass_staging_and_carry_no_credentials()
    {
        var defaults = new UaePassOptions();

        Assert.False(defaults.Enabled);
        Assert.Null(defaults.ClientId);
        Assert.Null(defaults.ClientSecret);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/authorize", defaults.AuthorizationEndpoint);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/token", defaults.TokenEndpoint);
        Assert.Equal("https://stg-id.uaepass.ae/idshub/userinfo", defaults.UserInfoEndpoint);
        Assert.Equal("http://localhost:5090/uaepass/callback", UaePassOptions.CallbackUri("http://localhost:5090/"));
    }

    [Theory]
    [InlineData("784199912345421", "784-••••-•••••42-1")]
    [InlineData("784-1999-1234542-1", "784-••••-•••••42-1")]
    [InlineData("784-1890-1497898-3", "784-••••-•••••98-3")]
    [InlineData("784000000000000", "784-••••-•••••00-0")]
    [InlineData("78419991234542", null)]
    [InlineData("7841999123454210", null)]
    [InlineData("123199912345421", null)]
    [InlineData("784x99912345421", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_emirates_id_keeps_only_the_prefix_two_digits_and_the_check_digit(string? value, string? expected) =>
        Assert.Equal(expected, ProfileMasking.EmiratesId(value));

    [Theory]
    [InlineData("971500000042", "+971•••••••42")]
    [InlineData("+971 50 123 4567", "+971•••••••67")]
    [InlineData("00971501234567", "+971•••••••67")]
    [InlineData("97151234003", "+971••••••03")]
    [InlineData("447700900123", "••••••••••23")]
    [InlineData("12345", null)]
    [InlineData("call me", null)]
    [InlineData(null, null)]
    public void A_mobile_number_keeps_the_uae_country_code_and_the_last_two_digits(string? value, string? expected) =>
        Assert.Equal(expected, ProfileMasking.Mobile(value));

    [Theory]
    [InlineData("test.person@example.ae", "t•••@example.ae")]
    [InlineData("a@b.ae", "a•••@b.ae")]
    [InlineData("@example.ae", null)]
    [InlineData("person@", null)]
    [InlineData("no-at-sign", null)]
    [InlineData("two words@example.ae", null)]
    [InlineData(null, null)]
    public void An_email_keeps_the_first_character_and_the_domain(string? value, string? expected) =>
        Assert.Equal(expected, ProfileMasking.Email(value));

    [Theory]
    [InlineData("Male", "male")]
    [InlineData("FEMALE", "female")]
    [InlineData("f", "female")]
    [InlineData("Unknown", null)]
    [InlineData(null, null)]
    public void Gender_becomes_the_openid_value_when_recognisable(string? value, string? expected) =>
        Assert.Equal(expected, ProfileMasking.Gender(value));

    [Fact]
    public void Userinfo_for_a_citizen_becomes_a_masked_profile_without_the_full_identifiers()
    {
        using var userInfo = JsonDocument.Parse(FakeUaePass.CitizenUserInfo);

        var identity = UaePassClient.ReadIdentity(userInfo.RootElement);

        Assert.NotNull(identity);
        Assert.Equal("11111111-2222-3333-4444-555555555555", identity.Uuid);
        Assert.Equal("uaepass-stg|11111111-2222-3333-4444-555555555555", identity.ToPersona().Subject);
        Assert.Equal(new[] { "participant" }, identity.ToPersona().Roles);
        var text = identity.Profile.ToString();
        Assert.DoesNotContain("784199912345421", text, StringComparison.Ordinal);
        Assert.DoesNotContain("971500000042", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test.person", text, StringComparison.Ordinal);
    }
}
