using System.Net;
using System.Text;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// Stands in for Google behind the provider's named HTTP client, so no test ever reaches the network. It answers the
/// token and userinfo endpoints with what the test set, and records each request it received.
/// </summary>
public sealed class FakeGoogle
{
    public const string AccessToken = "stub-google-access-token";

    /// <summary>Synthetic Google userinfo. Not a real person.</summary>
    public const string UserInfo = """
        {
          "sub": "109876543210987654321",
          "name": "Test Google Person",
          "given_name": "Test",
          "family_name": "Person",
          "picture": "https://lh3.googleusercontent.com/a/stub",
          "email": "test.google@example.com",
          "email_verified": true,
          "locale": "en"
        }
        """;

    private readonly Lock _gate = new();
    private readonly List<CapturedGoogleRequest> _requests = [];

    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    public string TokenBody { get; set; } =
        $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":3599,"scope":"openid email profile","id_token":"stub.id.token"}""";

    public HttpStatusCode UserInfoStatus { get; set; } = HttpStatusCode.OK;

    public string UserInfoBody { get; set; } = UserInfo;

    /// <summary>Fail every request as if the host could not be reached.</summary>
    public bool Unreachable { get; set; }

    public IReadOnlyList<CapturedGoogleRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>A new handler each time: the HTTP client factory disposes the handlers it rotates out.</summary>
    public HttpMessageHandler CreateHandler() => new Handler(this);

    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            _requests.Add(new CapturedGoogleRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        }

        if (Unreachable)
        {
            throw new HttpRequestException("Simulated: Google could not be reached.");
        }

        var (status, json) = (request.RequestUri!.Host, request.RequestUri.AbsolutePath) switch
        {
            ("oauth2.googleapis.com", "/token") => (TokenStatus, TokenBody),
            ("openidconnect.googleapis.com", "/v1/userinfo") => (UserInfoStatus, UserInfoBody),
            _ => (HttpStatusCode.NotFound, """{"error":"not_found"}"""),
        };
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class Handler(FakeGoogle owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            owner.AnswerAsync(request, cancellationToken);
    }
}
