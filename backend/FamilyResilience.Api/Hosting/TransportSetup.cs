using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FamilyResilience.Api.Hosting;

/// <summary>
/// TLS in deployed environments. HSTS and <see cref="HttpsRequiredMiddleware"/> run in deployed hosts only.
/// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are honoured only from the proxies listed in
/// <c>ReverseProxy:KnownProxies</c> (IP addresses) or <c>ReverseProxy:KnownNetworks</c> (CIDR ranges), one hop deep;
/// with neither, nothing is forwarded. Kestrel does not announce itself and limits what one request may send: endpoints
/// that expect more or less set their own <c>[RequestSizeLimit]</c>, and operators may override the limits under
/// <c>Kestrel:Limits</c>.
/// </summary>
public static class TransportSetup
{
    public const string KnownProxiesSetting = "ReverseProxy:KnownProxies";
    public const string KnownNetworksSetting = "ReverseProxy:KnownNetworks";

    /// <summary>1 MiB: ten times the largest content import (about 92 KB).</summary>
    public const long MaxRequestBodyBytes = 1024 * 1024;
    public const int MaxRequestHeadersTotalBytes = 32 * 1024;
    public const int MaxRequestLineBytes = 8 * 1024;

    public static WebApplicationBuilder AddTransport(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            options.Limits.MaxRequestHeadersTotalSize = MaxRequestHeadersTotalBytes;
            options.Limits.MaxRequestLineSize = MaxRequestLineBytes;
        });
        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });
        builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in Values(configuration, KnownProxiesSetting))
            {
                options.KnownProxies.Add(IPAddress.TryParse(proxy, out var address)
                    ? address
                    : throw new InvalidOperationException($"{KnownProxiesSetting} holds a value that is not an IP address."));
            }

            foreach (var network in Values(configuration, KnownNetworksSetting))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.TryParse(network, out var range)
                    ? range
                    : throw new InvalidOperationException($"{KnownNetworksSetting} holds a value that is not a CIDR range."));
            }
        });
        return builder;
    }

    public static WebApplication UseTransport(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (Values(app.Configuration, KnownProxiesSetting).Any() || Values(app.Configuration, KnownNetworksSetting).Any())
        {
            app.UseForwardedHeaders();
        }

        if (app.Environment.IsDeployed())
        {
            app.UseHsts();
            app.UseMiddleware<HttpsRequiredMiddleware>();
        }

        return app;
    }

    private static IEnumerable<string> Values(IConfiguration configuration, string key) =>
        configuration.GetSection(key).GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
}
