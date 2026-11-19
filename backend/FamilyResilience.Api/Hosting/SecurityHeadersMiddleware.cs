using Microsoft.Net.Http.Headers;

namespace FamilyResilience.Api.Hosting;

/// <summary>
/// Headers for every response, errors included. Responses carry participant data, so nothing may be cached by a
/// browser, proxy or the phone (<c>Cache-Control: no-store</c>); a JSON API is never framed, sniffed, or allowed to
/// load anything. An endpoint that sets one of these headers itself keeps its own value.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly (string Name, string Value)[] Defaults =
    [
        (HeaderNames.CacheControl, "no-store"),
        (HeaderNames.XContentTypeOptions, "nosniff"),
        (HeaderNames.XFrameOptions, "DENY"),
        ("Referrer-Policy", "no-referrer"),
        (HeaderNames.ContentSecurityPolicy, "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"),
    ];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            foreach (var (name, value) in Defaults)
            {
                if (!headers.ContainsKey(name))
                {
                    headers[name] = value;
                }
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}
