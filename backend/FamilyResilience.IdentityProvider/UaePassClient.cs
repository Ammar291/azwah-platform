using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The back channel to UAE PASS staging: exchanges the authorization code at the token endpoint (HTTP Basic client
/// authentication) and reads userinfo with the access token. The UAE PASS tokens are used once and dropped; nothing
/// is stored or logged but a fixed reason code. The named <see cref="HttpClientName"/> client has a 15-second timeout,
/// a 64 KiB response limit and no request logging.
/// </summary>
internal sealed class UaePassClient(HttpClient http, IOptions<IdentityProviderOptions> options)
{
    public const string HttpClientName = "uaepass";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const int MaxResponseBytes = 64 * 1024;

    private const int MaxUuidLength = 128;
    private const int MaxTextLength = 200;

    public async Task<UaePassOutcome> SignInAsync(string code, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var token = await ExchangeCodeAsync(settings, code, cancellationToken);
        if (token.AccessToken is not { } accessToken)
        {
            return UaePassOutcome.Failure(token.Failure!);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, settings.UaePass.UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var (json, failure) = await SendAsync(request, "userinfo", cancellationToken);
        if (json is not { } userInfo)
        {
            return UaePassOutcome.Failure(failure!);
        }

        using (userInfo)
        {
            return ReadIdentity(userInfo.RootElement) is { } identity
                ? UaePassOutcome.Success(identity)
                : UaePassOutcome.Failure("userinfo_without_uuid");
        }
    }

    /// <summary>The account and its masked profile; null without a usable <c>uuid</c>. Every other field is optional.</summary>
    public static UaePassIdentity? ReadIdentity(JsonElement userInfo)
    {
        if (userInfo.ValueKind != JsonValueKind.Object || Text(userInfo, "uuid", MaxUuidLength) is not { } uuid
            || !uuid.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            return null;
        }

        return new UaePassIdentity(uuid, new UpstreamProfile(
            UpstreamProfile.UaePassStagingSource,
            Name: Text(userInfo, "fullnameEN"),
            GivenName: Text(userInfo, "firstnameEN"),
            FamilyName: Text(userInfo, "lastnameEN"),
            Gender: ProfileMasking.Gender(Text(userInfo, "gender")),
            NameAr: Text(userInfo, "fullnameAR"),
            GivenNameAr: Text(userInfo, "firstnameAR"),
            FamilyNameAr: Text(userInfo, "lastnameAR"),
            NationalityEn: Text(userInfo, "nationalityEN"),
            NationalityAr: Text(userInfo, "nationalityAR"),
            UserType: Text(userInfo, "userType"),
            EmiratesIdMasked: ProfileMasking.EmiratesId(Text(userInfo, "idn")),
            MobileMasked: ProfileMasking.Mobile(Text(userInfo, "mobile")),
            EmailMasked: ProfileMasking.Email(Text(userInfo, "email"))));
    }

    private async Task<(string? AccessToken, string? Failure)> ExchangeCodeAsync(
        IdentityProviderOptions settings, string code, CancellationToken cancellationToken)
    {
        var uaePass = settings.UaePass;
        using var request = new HttpRequestMessage(HttpMethod.Post, uaePass.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", UaePassOptions.CallbackUri(settings.Issuer)),
            ]),
        };

        // RFC 6749 section 2.3.1: form-encode both values, then base64 "id:secret".
        var pair = $"{Uri.EscapeDataString(uaePass.ClientId!)}:{Uri.EscapeDataString(uaePass.ClientSecret!)}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
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
