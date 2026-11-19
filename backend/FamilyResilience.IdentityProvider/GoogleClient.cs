using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The back channel to Google: exchanges the authorization code at the token endpoint (client ID and secret in the
/// form, with the PKCE verifier) and reads userinfo with the access token. Google's tokens are used once and dropped;
/// nothing is stored or logged but a fixed reason code. The named <see cref="HttpClientName"/> client has a 15-second
/// timeout, a 64 KiB response limit and no request logging.
/// </summary>
internal sealed class GoogleClient(HttpClient http, IOptions<IdentityProviderOptions> options)
{
    public const string HttpClientName = "google";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const int MaxResponseBytes = 64 * 1024;

    private const int MaxSubjectLength = 255;
    private const int MaxTextLength = 200;

    public async Task<GoogleOutcome> SignInAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var token = await ExchangeCodeAsync(settings, code, codeVerifier, cancellationToken);
        if (token.AccessToken is not { } accessToken)
        {
            return GoogleOutcome.Failure(token.Failure!);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Google.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var (json, failure) = await SendAsync(request, "userinfo", cancellationToken);
        if (json is not { } userInfo)
        {
            return GoogleOutcome.Failure(failure!);
        }

        using (userInfo)
        {
            return ReadIdentity(userInfo.RootElement) is { } identity
                ? GoogleOutcome.Success(identity)
                : GoogleOutcome.Failure("userinfo_without_sub");
        }
    }

    /// <summary>
    /// The account and its masked profile; null without a usable <c>sub</c>. The e-mail address is kept (masked) only
    /// when Google says it is verified. Every other field is optional.
    /// </summary>
    public static GoogleIdentity? ReadIdentity(JsonElement userInfo)
    {
        if (userInfo.ValueKind != JsonValueKind.Object || Text(userInfo, "sub", MaxSubjectLength) is not { } subject
            || !subject.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        var emailVerified = userInfo.TryGetProperty("email_verified", out var verified)
            && (verified.ValueKind == JsonValueKind.True
                || (verified.ValueKind == JsonValueKind.String && string.Equals(verified.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
        return new GoogleIdentity(subject, new UpstreamProfile(
            UpstreamProfile.GoogleSource,
            Name: Text(userInfo, "name"),
            GivenName: Text(userInfo, "given_name"),
            FamilyName: Text(userInfo, "family_name"),
            Gender: null,
            NameAr: null,
            GivenNameAr: null,
            FamilyNameAr: null,
            NationalityEn: null,
            NationalityAr: null,
            UserType: null,
            EmiratesIdMasked: null,
            MobileMasked: null,
            EmailMasked: emailVerified ? ProfileMasking.Email(Text(userInfo, "email")) : null));
    }

    private async Task<(string? AccessToken, string? Failure)> ExchangeCodeAsync(
        IdentityProviderOptions settings, string code, string codeVerifier, CancellationToken cancellationToken)
    {
        var google = settings.Google;
        using var request = new HttpRequestMessage(HttpMethod.Post, google.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", GoogleOptions.CallbackUri(settings.Issuer)),
                new("client_id", google.ClientId!),
                new("client_secret", google.ClientSecret!),
                new("code_verifier", codeVerifier),
            ]),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var (json, failure) = await SendAsync(request, "token", cancellationToken);
        if (json is null)
        {
            return (null, failure);
        }

        using (json)
        {
            var accessToken = json.RootElement.ValueKind == JsonValueKind.Object ? Text(json.RootElement, "access_token", 8192) : null;
            return accessToken is null ? (null, "token_invalid") : (accessToken, null);
        }
    }

    /// <summary>The JSON answer, or a reason code: <c>{step}_rejected</c>, <c>_invalid</c>, <c>_timeout</c>, <c>_unreachable</c>.</summary>
    private async Task<(JsonDocument? Json, string? Failure)> SendAsync(
        HttpRequestMessage request, string step, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"{step}_rejected");
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return (JsonDocument.Parse(body), null);
        }
        catch (JsonException)
        {
            return (null, $"{step}_invalid");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"{step}_timeout");
        }
        catch (HttpRequestException)
        {
            return (null, $"{step}_unreachable");
        }
    }

    private static string? Text(JsonElement json, string name, int maxLength = MaxTextLength) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text && text.Length <= maxLength
            ? text
            : null;
}
