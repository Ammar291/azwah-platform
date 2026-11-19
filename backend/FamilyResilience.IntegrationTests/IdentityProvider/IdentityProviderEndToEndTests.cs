using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// The real token path: the development identity provider issues a token through authorize and token, and the API's
/// own JWT bearer handler (not the test scheme) validates it with keys fetched from the provider's discovery document.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityProviderEndToEndTests(ApiFactory api)
{
    private static WebApplicationFactory<Program> ApiTrusting(ApiFactory api, IdentityProviderFactory provider) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Runs after the test host's own registration, so the real bearer scheme is the default again.
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            });
            // Configure (not PostConfigure): the framework builds the metadata manager from these in its PostConfigure.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = provider.IssuerUrl;
                options.RequireHttpsMetadata = false;
                options.BackchannelHttpHandler = provider.Server.CreateHandler();
                options.Audience = "azwah-api";
                options.TokenValidationParameters.ValidAudience = "azwah-api";
                // The provider's tokens carry real timestamps, while the test host pins its TimeProvider to the test
                // data's date: keep the bearer handler on real time explicitly.
                options.TimeProvider = TimeProvider.System;
            });
        }));

    private static async Task<HttpResponseMessage> MeAsync(WebApplicationFactory<Program> host, string accessToken)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task End_to_end_the_api_accepts_a_development_token()
    {
        await using var provider = new IdentityProviderFactory();
        using var providerClient = provider.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(providerClient, "researcher", TestContext.Current.CancellationToken);
        await using var host = ApiTrusting(api, provider);

        using var response = await MeAsync(host, signedIn.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("dev-researcher", me!.Subject);
        Assert.Equal(new[] { "researcher" }, me.Roles);
    }

    [Fact]
    public async Task End_to_end_the_api_accepts_a_refreshed_token_and_refuses_a_tampered_one()
    {
        await using var provider = new IdentityProviderFactory();
        using var providerClient = provider.CreateClient();
        var signedIn = await IdentityProviderFlow.SignInAsync(providerClient, "participant_lead", TestContext.Current.CancellationToken);
        using var refresh = await IdentityProviderFlow.RefreshAsync(
            providerClient, ClientCredentials.Mobile, signedIn.RefreshToken, TestContext.Current.CancellationToken);
        var refreshed = (await IdentityProviderFlow.ReadJsonAsync(refresh, TestContext.Current.CancellationToken))
            .GetProperty("access_token").GetString()!;
        var parts = refreshed.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{IdentityProviderFlow.RandomValue()}";
        await using var host = ApiTrusting(api, provider);

        using var accepted = await MeAsync(host, refreshed);
        using var refused = await MeAsync(host, tampered);
        using var idTokenAsAccess = await MeAsync(host, signedIn.IdToken);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var me = await accepted.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("dev-participant-lead", me!.Subject);
        Assert.Equal(new[] { "participant" }, me.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, idTokenAsAccess.StatusCode);
    }

    [Fact]
    public async Task End_to_end_the_api_accepts_a_uae_pass_token_from_a_deployed_provider()
    {
        await using var provider = IdentityProviderFactory.DeployedFactory();
        using var providerClient = provider.CreateClient();
        var tokens = await IdentityProviderFlow.SignInWithUaePassAsync(providerClient, TestContext.Current.CancellationToken);
        await using var host = ApiTrusting(api, provider);

        using var response = await MeAsync(host, tokens.GetProperty("access_token").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("uaepass-stg|11111111-2222-3333-4444-555555555555", me!.Subject);
        Assert.Equal(new[] { "participant" }, me.Roles);
    }

    [Fact]
    public async Task End_to_end_the_api_accepts_a_google_token_as_a_participant()
    {
        await using var provider = new IdentityProviderFactory { GoogleEnabled = true };
        using var providerClient = provider.CreateClient();
        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(providerClient, TestContext.Current.CancellationToken);
        await using var host = ApiTrusting(api, provider);

        using var response = await MeAsync(host, tokens.GetProperty("access_token").GetString()!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("google|109876543210987654321", me!.Subject);
        Assert.Equal(new[] { "participant" }, me.Roles);
    }

    [Fact]
    public async Task End_to_end_the_api_accepts_a_google_staff_token_with_several_roles_and_grants_each_roles_permissions()
    {
        await using var provider = new IdentityProviderFactory
        {
            GoogleEnabled = true,
            GoogleStaffRoles = ["system_administrator", "program_administrator", "researcher", "support_coordinator"],
            AdminGoogleStaffSignIn = true,
        };
        using var providerClient = provider.CreateClient();
        var tokens = await IdentityProviderFlow.SignInWithGoogleAsync(
            providerClient, TestContext.Current.CancellationToken, ClientCredentials.AdminBasic());
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        await using var host = ApiTrusting(api, provider);

        using var response = await MeAsync(host, accessToken);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var permissions = await client.GetAsync("/api/v1/me/permissions", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(TestContext.Current.CancellationToken);
        Assert.Equal("google-staff|109876543210987654321", me!.Subject);
        Assert.Equal(
            new[] { "program_administrator", "researcher", "support_coordinator", "system_administrator" },
            me.Roles.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        var granted = (await permissions.Content.ReadFromJsonAsync<PermissionsBody>(TestContext.Current.CancellationToken))!.Permissions;
        Assert.Contains("content.manage", granted);
        Assert.Contains("participation.read", granted);
        Assert.Contains("exports.manage", granted);
        Assert.Contains("referrals.manage", granted);
        Assert.Contains("safety_alerts.read", granted);
        Assert.DoesNotContain("households.create", granted);
    }

    private sealed record MeBody(string Subject, string[] Roles);

    private sealed record PermissionsBody(string[] Permissions);
}
