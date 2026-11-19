using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// Real JWT bearer tokens for the API's own JwtBearer handler: an API host whose default scheme is Bearer again, trusting
/// one RSA key and one symmetric key for <see cref="Issuer"/> and <see cref="Audience"/>, on the system clock.
/// </summary>
internal static class BearerTokens
{
    public const string Issuer = "https://issuer.test";
    public const string Audience = "azwah-api";

    public static readonly RsaSecurityKey SigningKey = NewRsaKey("trusted");
    public static readonly RsaSecurityKey UntrustedKey = NewRsaKey("untrusted");
    public static readonly SymmetricSecurityKey SymmetricKey = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "symmetric" };

    public static WebApplicationFactory<Program> BearerHost(this ApiFactory factory, ILoggerProvider? logs = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
                    ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audience,
                }));
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(TimeProvider.System);
                services.Configure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                });
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.TokenValidationParameters.IssuerSigningKeys = [SigningKey, SymmetricKey]);
            });
        });

    public static string Create(
        string subject,
        string[] roles,
        SecurityKey? key = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTimeOffset? expires = null,
        bool withExpiry = true)
    {
        var now = DateTimeOffset.UtcNow;
        var signingKey = key ?? SigningKey;
        var algorithm = signingKey is SymmetricSecurityKey ? SecurityAlgorithms.HmacSha256 : SecurityAlgorithms.RsaSha256;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.AddMinutes(-5).UtcDateTime,
            NotBefore = now.AddMinutes(-5).UtcDateTime,
            Expires = withExpiry ? (expires ?? now.AddMinutes(10)).UtcDateTime : null,
            Claims = new Dictionary<string, object> { ["sub"] = subject, ["role"] = roles },
            SigningCredentials = new SigningCredentials(signingKey, algorithm),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = withExpiry }.CreateToken(descriptor);
    }

    /// <summary>A token with <c>alg: none</c> and no signature.</summary>
    public static string Unsigned(string subject)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return $"{Base64Url("""{"alg":"none","typ":"JWT"}""")}."
            + $"{Base64Url($$"""{"iss":"{{Issuer}}","aud":"{{Audience}}","sub":"{{subject}}","exp":{{expires}}}""")}.";
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));

    private static RsaSecurityKey NewRsaKey(string id) => new(RSA.Create(2048)) { KeyId = id };
}
