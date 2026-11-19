using System.Net;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed class IdentityProviderHostTests
{
    private const string SensitiveDetail = "sensitive-detail-that-must-not-leak";
    private const int StartedEvent = 3000;
    private const int ClientWithoutSecretEvent = 3001;

    [Fact]
    public async Task An_unhandled_error_answers_a_generic_server_error_without_details()
    {
        await using var provider = new IdentityProviderFactory();
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter, ThrowingEndpoint>()));
        using var client = host.CreateClient(new() { BaseAddress = new Uri(IdentityProviderFactory.Issuer), AllowAutoRedirect = false });

        using var response = await client.GetAsync(ThrowingEndpoint.Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("""{"error":"server_error","error_description":"The identity provider failed."}""", body);
        Assert.DoesNotContain(SensitiveDetail, body, StringComparison.Ordinal);
        // The framework's exception middleware sends "no-cache, no-store" here.
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task It_warns_at_start_up_that_sign_in_is_simulated_and_that_the_admin_client_has_no_secret()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = new IdentityProviderFactory { ConfiguredAdminClientSecret = "" };
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));

        using var client = host.CreateClient();

        Assert.Contains(logs.Entries, entry => entry is { EventId: StartedEvent, Level: LogLevel.Warning });
        Assert.Contains(logs.Entries, entry => entry is { EventId: ClientWithoutSecretEvent, Level: LogLevel.Warning });
    }

    [Fact]
    public async Task It_does_not_warn_about_the_admin_client_once_its_secret_is_configured()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = new IdentityProviderFactory();
        await using var host = provider.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));

        using var client = host.CreateClient();

        Assert.Contains(logs.Entries, entry => entry.EventId == StartedEvent);
        Assert.DoesNotContain(logs.Entries, entry => entry.EventId == ClientWithoutSecretEvent);
    }

    /// <summary>A route that throws, added after the provider's own pipeline so its error handling applies.</summary>
    private sealed class ThrowingEndpoint : IStartupFilter
    {
        public const string Path = "/test-only/throw";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Path, branch => branch.Run(_ => throw new InvalidOperationException(SensitiveDetail)));
        };
    }
}
