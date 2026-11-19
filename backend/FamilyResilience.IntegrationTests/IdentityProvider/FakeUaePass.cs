using System.Net;
using System.Text;

namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>
/// Stands in for UAE PASS staging behind the provider's named HTTP client, so no test ever reaches the network. It
/// answers the token and userinfo endpoints with what the test set, and records each request it received.
/// </summary>
public sealed class FakeUaePass
{
    public const string AccessToken = "stub-uaepass-access-token";

    /// <summary>Synthetic staging-style userinfo for a citizen with SOP3. Not a real person.</summary>
    public const string CitizenUserInfo = """
        {
          "sub": "11111111-2222-3333-4444-555555555555",
          "uuid": "11111111-2222-3333-4444-555555555555",
          "userType": "SOP3",
          "idn": "784199912345421",
          "idType": "ID",
          "firstnameEN": "Test",
          "firstnameAR": "تجربة",
          "lastnameEN": "Person",
          "lastnameAR": "شخص",
          "fullnameEN": "Test Staging Person",
          "fullnameAR": "تجربة شخص",
          "nationalityEN": "ARE",
          "nationalityAR": "الإمارات",
          "gender": "Male",
          "mobile": "971500000042",
          "email": "test.person@example.ae",
          "titleEN": "Mr",
          "titleAR": "السيد"
        }
        """;

    private readonly Lock _gate = new();
    private readonly List<CapturedUaePassRequest> _requests = [];

    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    public string TokenBody { get; set; } = $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":3600,"scope":"urn:uae:digitalid:profile:general"}""";

    public HttpStatusCode UserInfoStatus { get; set; } = HttpStatusCode.OK;

    public string UserInfoBody { get; set; } = CitizenUserInfo;

    /// <summary>Fail every request as if the host could not be reached.</summary>
    public bool Unreachable { get; set; }

    public IReadOnlyList<CapturedUaePassRequest> Requests
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
            _requests.Add(new CapturedUaePassRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
        }

        if (Unreachable)
        {
            throw new HttpRequestException("Simulated: UAE PASS staging could not be reached.");
        }

        var (status, json) = request.RequestUri!.AbsolutePath switch
        {
            "/idshub/token" => (TokenStatus, TokenBody),
            "/idshub/userinfo" => (UserInfoStatus, UserInfoBody),
            _ => (HttpStatusCode.NotFound, """{"error":"not_found"}"""),
        };
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class Handler(FakeUaePass owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            owner.AnswerAsync(request, cancellationToken);
    }
}
