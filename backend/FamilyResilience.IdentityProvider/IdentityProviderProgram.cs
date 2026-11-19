using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The Azwah identity provider: an OpenID Connect provider that signs people in with UAE PASS (staging, until
/// production onboarding). In the Development environment it also offers simulated personas, with no identity checked;
/// anywhere else UAE PASS is the sign-in and start-up refuses unsafe settings (<see cref="ProviderMode"/>, D-129). Google
/// is an optional second upstream in both modes (<see cref="GoogleEndpoints"/>, D-147).
/// The API never references it.
/// </summary>
/// <remarks>
/// An explicit Main (no top-level statements): the integration tests reference this assembly next to the API, whose
/// top-level statements already define the global <c>Program</c>.
/// </remarks>
public static class IdentityProviderProgram
{
    public static async Task Main(string[] args)
    {
        // RunAsync disposes the host when it stops.
        var app = CreateApp(args);
        await app.RunAsync();
    }

    public static WebApplication CreateApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 64 * 1024;
        });

        var services = builder.Services;
        services.AddOptions<IdentityProviderOptions>()
            .Bind(builder.Configuration.GetSection(IdentityProviderOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityProviderOptions>, IdentityProviderOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ProviderMode>();
        services.AddSingleton<Pages>();
        services.AddSingleton<SigningKey>();
        services.AddSingleton<ClientRegistry>();
        services.AddSingleton<ClientAuthenticator>();
        services.AddSingleton<GrantStore>();
        services.AddSingleton<TokenIssuer>();
        services.AddSingleton<PendingUpstreamSignIns>();
        services.AddSingleton<PendingSignOuts>();
        // UAE PASS staging back channel: bounded in time and size, and no request logging (URLs and headers stay out).
        services.AddHttpClient<UaePassClient>(UaePassClient.HttpClientName, client =>
            {
                client.Timeout = UaePassClient.Timeout;
                client.MaxResponseContentBufferSize = UaePassClient.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        // Google back channel (D-147): the same bounds, and no request logging either.
        services.AddHttpClient<GoogleClient>(GoogleClient.HttpClientName, client =>
            {
                client.Timeout = GoogleClient.Timeout;
                client.MaxResponseContentBufferSize = GoogleClient.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddHostedService<StartupAnnouncement>();

        var app = builder.Build();

        // Generic answers only: no exception page, message or stack trace ever reaches a client.
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = WriteServerErrorAsync });
        app.Use(AddSecurityHeaders);
        app.MapDiscovery();
        app.MapAuthorize();
        app.MapUaePass();
        app.MapGoogle();
        app.MapToken();
        app.MapRevocation();
        app.MapEndSession();
        return app;
    }

    /// <summary>Every response, unless an endpoint set the header itself (pages set their own policy).</summary>
    private static Task AddSecurityHeaders(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            if (headers.CacheControl.Count == 0)
            {
                headers.CacheControl = "no-store";
            }

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (headers.ContentSecurityPolicy.Count == 0)
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }

    // The exception middleware has already logged the exception; the client learns nothing about it.
    private static Task WriteServerErrorAsync(HttpContext context) => OAuthJson.ServerError().ExecuteAsync(context);
}
