using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Access tokens (RFC 9068 JWTs for the API) and ID tokens (OpenID Connect Core 2). Access tokens never carry names or
/// any other personal claim: the subject and roles are all the platform needs. Only the ID token of a UAE PASS staging
/// sign-in carries a profile, masked, for the app to show the person (D-96). In Development both carry
/// <c>"azwah_development_only": true</c>, which a deployed API refuses; a deployed provider's tokens carry no such
/// marker (D-129).
/// </summary>
internal sealed class TokenIssuer(SigningKey key, IOptions<IdentityProviderOptions> options, TimeProvider time, ProviderMode mode)
{
    public int AccessTokenSeconds => options.Value.AccessTokenSeconds;

    public string CreateAccessToken(Persona persona, string clientId, string scope)
    {
        var settings = options.Value;
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var claims = new JsonObject
        {
            ["iss"] = settings.Issuer,
            ["sub"] = persona.Subject,
            ["aud"] = settings.Audience,
            ["exp"] = now + settings.AccessTokenSeconds,
            ["iat"] = now,
            ["nbf"] = now,
            ["jti"] = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            ["client_id"] = clientId,
            ["scope"] = scope,
            ["role"] = new JsonArray([.. persona.Roles.Select(role => (JsonNode?)JsonValue.Create(role))]),
        };
        MarkDevelopment(claims);
        return key.CreateJwt("at+jwt", claims);
    }

    /// <param name="grant">The sign-in; an upstream profile adds its (masked) claims.</param>
    /// <param name="forRefresh">
    /// A refresh's ID token has no <c>nonce</c> and keeps the original <c>auth_time</c> (OpenID Connect Core 12.2).
    /// </param>
    public string CreateIdToken(AuthorizationGrant grant, bool forRefresh = false)
    {
        var settings = options.Value;
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var claims = new JsonObject
        {
            ["iss"] = settings.Issuer,
            ["sub"] = grant.Persona.Subject,
            ["aud"] = grant.ClientId,
            ["azp"] = grant.ClientId,
            ["exp"] = now + settings.AccessTokenSeconds,
            ["iat"] = now,
            ["auth_time"] = grant.AuthTime.ToUnixTimeSeconds(),
        };
        if (!forRefresh)
        {
            claims["nonce"] = grant.Nonce;
        }

        if (grant.Profile is { } profile)
        {
            AddProfile(claims, profile);
        }

        MarkDevelopment(claims);
        return key.CreateJwt("JWT", claims);
    }

    private void MarkDevelopment(JsonObject claims)
    {
        if (mode.DevelopmentMarkers)
        {
            claims["azwah_development_only"] = true;
        }
    }

    /// <summary>
    /// Standard claims for the English name and gender, and one <c>azwah_profile</c> object for the rest. Absent values
    /// are omitted. Identity numbers, phone numbers and e-mail addresses arrive masked (<see cref="ProfileMasking"/>).
    /// </summary>
    private static void AddProfile(JsonObject claims, UpstreamProfile profile)
    {
        AddIfPresent(claims, "name", profile.Name);
        AddIfPresent(claims, "given_name", profile.GivenName);
        AddIfPresent(claims, "family_name", profile.FamilyName);
        AddIfPresent(claims, "gender", profile.Gender);
        var details = new JsonObject { ["source"] = profile.Source };
        AddIfPresent(details, "name_ar", profile.NameAr);
        AddIfPresent(details, "given_name_ar", profile.GivenNameAr);
        AddIfPresent(details, "family_name_ar", profile.FamilyNameAr);
        AddIfPresent(details, "nationality_en", profile.NationalityEn);
        AddIfPresent(details, "nationality_ar", profile.NationalityAr);
        AddIfPresent(details, "user_type", profile.UserType);
        AddIfPresent(details, "emirates_id_masked", profile.EmiratesIdMasked);
        AddIfPresent(details, "mobile_masked", profile.MobileMasked);
        AddIfPresent(details, "email_masked", profile.EmailMasked);
        claims["azwah_profile"] = details;
    }

    private static void AddIfPresent(JsonObject json, string name, string? value)
    {
        if (value is not null)
        {
            json[name] = value;
        }
    }
}
