namespace FamilyResilience.IdentityProvider;

/// <summary>An HTML page with its own Content-Security-Policy (no scripts, inline styles only).</summary>
internal sealed class HtmlPage(int statusCode, string html, string contentSecurityPolicy = HtmlPage.SecurityPolicy) : IResult
{
    public const string SecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>
    /// The persona page's policy. Browsers check form-action against the redirect that follows a form post too, so the
    /// already validated redirect target (its origin, or its scheme for app links) is allowed beside 'self'.
    /// </summary>
    /// <param name="redirectUri">The validated client redirect URI.</param>
    public static string SecurityPolicyReturningTo(string redirectUri)
    {
        var target = new Uri(redirectUri);
        var source = target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps
            ? target.GetLeftPart(UriPartial.Authority)
            : $"{target.Scheme}:";
        return $"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {source}; frame-ancestors 'none'";
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.ContentSecurityPolicy = contentSecurityPolicy;
        response.Headers.CacheControl = "no-store";
        await response.WriteAsync(html, httpContext.RequestAborted);
    }
}
