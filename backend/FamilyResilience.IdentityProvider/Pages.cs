using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The provider's pages: plain HTML with inline styles and no scripts. In Development every page says it is simulated
/// and the persona page exists; deployed, pages are neutral and list no personas (<see cref="ProviderMode"/>). Every
/// value that came from a request is HTML-encoded.
/// </summary>
internal sealed class Pages(ProviderMode mode)
{
    public const string Label = "Development identity provider — simulated. No identity is checked.";

    private const string Styles = """
        :root { color-scheme: light dark; --ink: #1d2b27; --paper: #f7f5ef; --pine: #1f5c4a; --warn: #8a4b00; --line: #c9c3b3; }
        @media (prefers-color-scheme: dark) { :root { --ink: #e9e6dc; --paper: #14201c; --pine: #7fc8ad; --warn: #f0b35a; --line: #3d4a45; } }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 24px 16px; background: var(--paper); color: var(--ink); font: 16px/1.5 system-ui, sans-serif; }
        main { max-width: 640px; margin: 0 auto; }
        .banner { margin: 0 0 24px; padding: 12px 16px; border: 2px solid var(--warn); border-radius: 8px; }
        .banner strong { color: var(--warn); }
        h1 { font-size: 1.5rem; margin: 0 0 8px; }
        code { overflow-wrap: anywhere; }
        ul { list-style: none; margin: 16px 0; padding: 0; display: grid; gap: 8px; }
        button { width: 100%; min-height: 48px; padding: 10px 16px; border-radius: 8px; font: inherit; text-align: start; cursor: pointer; }
        .persona { border: 1px solid var(--pine); background: transparent; color: var(--ink); }
        .persona:hover, .persona:focus-visible { outline: 3px solid var(--pine); outline-offset: 1px; }
        .persona span, .upstream span { display: block; font-size: 0.875rem; opacity: 0.8; }
        .upstream { margin: 16px 0 0; border: 2px solid var(--pine); background: var(--pine); color: var(--paper); }
        a.upstream { display: block; min-height: 48px; padding: 10px 16px; border-radius: 8px; text-decoration: none; }
        .upstream:hover, .upstream:focus-visible { outline: 3px solid var(--warn); outline-offset: 1px; }
        .cancel { border: 1px solid var(--line); background: transparent; color: var(--ink); text-align: center; }
        table { border-collapse: collapse; width: 100%; }
        th, td { padding: 6px 8px; border-bottom: 1px solid var(--line); text-align: start; }
        """;

    /// <summary>The persona page (Development only: deployed, /authorize never shows a page for a valid request).</summary>
    public HtmlPage PersonaPicker(AuthorizationRequest request, string formAction, UaePassOptions uaePass, GoogleOptions google)
    {
        ArgumentNullException.ThrowIfNull(uaePass);
        ArgumentNullException.ThrowIfNull(google);
        var body = new StringBuilder();
        body.Append("<h1>Choose a test persona</h1>")
            .Append("<p>Signing in to <code>").Append(Encode(request.Client.Id)).Append("</code>. The result returns to <code>")
            .Append(Encode(request.RedirectUri)).Append("</code>.</p>")
            .Append("<form method=\"post\" action=\"").Append(Encode(formAction)).Append("\">");
        Hidden(body, "client_id", request.Client.Id);
        Hidden(body, "redirect_uri", request.RedirectUri);
        Hidden(body, "response_type", "code");
        Hidden(body, "scope", request.RequestedScope);
        Hidden(body, "state", request.State);
        Hidden(body, "nonce", request.Nonce);
        Hidden(body, "code_challenge", request.CodeChallenge);
        Hidden(body, "code_challenge_method", "S256");
        if (request.UiLocales is not null)
        {
            Hidden(body, "ui_locales", request.UiLocales);
        }

        if (uaePass.IsActive)
        {
            // A link, not a submit button: browsers apply form-action to every redirect that follows a form post, and
            // UAE PASS redirects on to hosts of its own choosing (stg-id to stg-ids), which no policy here can list.
            body.Append("<a class=\"upstream\" href=\"").Append(Encode(UpstreamLink(request, formAction, AuthorizationRequest.UaePassHint)))
                .Append("\">Continue with UAE PASS (staging)<span>A real UAE PASS staging test account, approved in the UAE PASS staging app. The app receives a masked profile; the full Emirates ID never leaves this provider.</span></a>");
        }

        if (google.IsActive)
        {
            // A link for the same reason: Google's sign-in moves between hosts of its own. A client that opts in to Google
            // staff sign-in (Development only, D-149) says which roles the account receives.
            var signedInAs = google.StaffRolesFor(request.Client, mode) is { } staffRoles
                ? $"signed in as staff with roles: {string.Join(", ", staffRoles)}"
                : "signed in as a participant";
            body.Append("<a class=\"upstream\" href=\"").Append(Encode(UpstreamLink(request, formAction, AuthorizationRequest.GoogleHint)))
                .Append("\">Continue with Google<span>A real Google account, ").Append(Encode(signedInAs))
                .Append(". The app receives the name and a masked e-mail address.</span></a>");
        }

        if (uaePass.IsActive || google.IsActive)
        {
            body.Append("<p>Or choose a simulated persona:</p>");
        }

        body.Append("<ul>");
        foreach (var persona in Personas.All)
        {
            body.Append("<li><button class=\"persona\" type=\"submit\" name=\"persona\" value=\"").Append(Encode(persona.Code))
                .Append("\">").Append(Encode(persona.Description))
                .Append("<span>").Append(Encode(persona.Subject)).Append(" · roles: ").Append(Encode(string.Join(", ", persona.Roles)))
                .Append("</span></button></li>");
        }

        body.Append("</ul>")
            .Append("<button class=\"cancel\" type=\"submit\" name=\"cancel\" value=\"cancel\">Cancel</button>")
            .Append("</form>");
        return new HtmlPage(
            StatusCodes.Status200OK,
            Document("Choose a test persona", body.ToString()),
            HtmlPage.SecurityPolicyReturningTo(request.RedirectUri));
    }

    /// <summary>
    /// The same validated request as a GET with <c>idp_hint</c> (<c>uae_pass</c> or <c>google</c>), which /authorize
    /// sends to that upstream sign-in.
    /// </summary>
    private static string UpstreamLink(AuthorizationRequest request, string authorizePath, string hint)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = request.Client.Id,
            ["redirect_uri"] = request.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = request.RequestedScope,
            ["state"] = request.State,
            ["nonce"] = request.Nonce,
            ["code_challenge"] = request.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["idp_hint"] = hint,
        };
        if (request.UiLocales is not null)
        {
            query["ui_locales"] = request.UiLocales;
        }

        return QueryHelpers.AddQueryString(authorizePath, query);
    }

    /// <summary>A refused request. Nothing from the request is shown, and nothing redirects.</summary>
    public HtmlPage Refused(string message) => new(
        StatusCodes.Status400BadRequest,
        Document(
            "Sign-in request refused",
            $"<h1>Sign-in request refused</h1><p>{Encode(message)}</p><p>No redirect was made. Check the client ID and the redirect addresses registered in the provider's configuration.</p>"));

    public HtmlPage SignedOut() => new(
        StatusCodes.Status200OK,
        Document("Signed out", "<h1>Signed out</h1><p>This provider keeps no session, so there is nothing more to end here. You can close this page.</p>"));

    public HtmlPage Home(string issuer, UaePassOptions uaePass, GoogleOptions google)
    {
        ArgumentNullException.ThrowIfNull(uaePass);
        ArgumentNullException.ThrowIfNull(google);
        var content = new StringBuilder();
        var title = mode.PersonasAvailable ? "Development identity provider" : "Azwah identity provider";
        content.Append("<h1>").Append(title).Append("</h1><p>Issuer <code>").Append(Encode(issuer))
            .Append("</code>. Discovery: <code>/.well-known/openid-configuration</code>.</p>");
        content.Append(uaePass.IsActive
            ? "<p>Sign-in with <strong>UAE PASS (staging)</strong>: test accounts from the UAE PASS staging app. Production UAE PASS is not configured.</p>"
            : "<p>UAE PASS (staging) sign-in is not enabled.</p>");
        content.Append(google.IsActive
            ? "<p>Sign-in with <strong>Google</strong>: Google accounts, as participants.</p>"
            : "<p>Google sign-in is not enabled.</p>");
        if (mode.PersonasAvailable)
        {
            content.Append("<table><thead><tr><th scope=\"col\">Persona</th><th scope=\"col\">Subject</th><th scope=\"col\">Roles</th></tr></thead><tbody>");
            foreach (var persona in Personas.All)
            {
                content.Append("<tr><td><code>").Append(Encode(persona.Code)).Append("</code></td><td><code>")
                    .Append(Encode(persona.Subject)).Append("</code></td><td>").Append(Encode(string.Join(", ", persona.Roles)))
                    .Append("</td></tr>");
            }

            content.Append("</tbody></table>");
        }

        return new HtmlPage(StatusCodes.Status200OK, Document(title, content.ToString()));
    }

    private static void Hidden(StringBuilder body, string name, string value) =>
        body.Append("<input type=\"hidden\" name=\"").Append(Encode(name)).Append("\" value=\"").Append(Encode(value)).Append("\">");

    private string Document(string title, string content)
    {
        var banner = mode.DevelopmentMarkers
            ? $"""<p class="banner" role="note"><strong>{Label}</strong> For local development with synthetic data only: anyone can sign in as any persona. Never use it with real families or in a deployed environment.</p>"""
            : string.Empty;
        var fullTitle = mode.DevelopmentMarkers ? $"{Encode(title)} · development only" : $"{Encode(title)} · Azwah";
        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="referrer" content="no-referrer">
            <title>{fullTitle}</title>
            <style>{Styles}</style>
            </head>
            <body>
            <main>
            {banner}
            {content}
            </main>
            </body>
            </html>
            """;
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
