using System.Security.Claims;
using System.Security.Cryptography;
using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Observability;
using FamilyResilience.UnitTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FamilyResilience.UnitTests.Api;

public sealed class TokenValidationTests
{
    private static readonly string[] Asymmetric =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    [Fact]
    public void Development_allows_hs256_for_user_jwts_and_the_asymmetric_algorithms()
    {
        var allowed = TokenValidation.AllowedAlgorithms(new StubHostEnvironment("Development"));

        Assert.Equal([.. Asymmetric, SecurityAlgorithms.HmacSha256], allowed);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Every_other_host_allows_only_asymmetric_algorithms(string environment)
    {
        var allowed = TokenValidation.AllowedAlgorithms(new StubHostEnvironment(environment));

        Assert.Equal(Asymmetric, allowed);
        Assert.DoesNotContain(SecurityAlgorithms.HmacSha256, allowed);
        Assert.DoesNotContain("none", allowed);
    }

    public static TheoryData<Exception, string> Rejections => new()
    {
        { new SecurityTokenExpiredException(), "expired" },
        { new SecurityTokenInvalidSignatureException(), "signature" },
        { new SecurityTokenSignatureKeyNotFoundException(), "signature" },
        { new SecurityTokenInvalidIssuerException(), "issuer" },
        { new SecurityTokenInvalidAudienceException(), "audience" },
        { new SecurityTokenNotYetValidException(), "lifetime" },
        { new SecurityTokenNoExpirationException(), "lifetime" },
        { new SecurityTokenInvalidLifetimeException(), "lifetime" },
        { new SecurityTokenInvalidAlgorithmException(), "algorithm" },
        { new SecurityTokenMalformedException(), "malformed" },
        { new ArgumentException("not a token"), "malformed" },
        { new InvalidOperationException("metadata unavailable"), "other" },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Each_rejection_maps_to_a_fixed_reason(Exception exception, string reason) =>
        Assert.Equal(reason, TokenValidation.RejectionReason(exception));

    [Theory]
    [InlineData(null, 60)]
    [InlineData("0", 0)]
    [InlineData("45", 45)]
    [InlineData("-5", 0)]
    [InlineData("100000", 300)]
    [InlineData("soon", 60)]
    public void The_clock_skew_defaults_to_a_minute_and_stays_within_five(string? configured, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), TokenValidation.ClockSkew(configured));

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", false)]
    [InlineData("Production", false)]
    public async Task An_hs256_token_with_a_trusted_key_validates_only_in_development(string environment, bool valid)
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var options = new JwtBearerOptions();
        options.TokenValidationParameters.ValidIssuer = "issuer";
        options.TokenValidationParameters.ValidAudience = "audience";
        options.TokenValidationParameters.IssuerSigningKey = key;
        TokenValidation.Apply(options, new StubHostEnvironment(environment), new ConfigurationBuilder().Build());
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "issuer",
            Audience = "audience",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, options.TokenValidationParameters);

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Development", true, true)]
    [InlineData("Testing", true, true)]
    [InlineData("Production", false, true)]
    public async Task A_development_token_signs_no_one_in_outside_development_and_testing(string environment, bool marked, bool accepted)
    {
        var options = new JwtBearerOptions();
        TokenValidation.Apply(options, new StubHostEnvironment(environment), new ConfigurationBuilder().Build());
        var claims = new List<Claim> { new("sub", "dev-system-administrator") };
        if (marked)
        {
            claims.Add(new Claim(TokenValidation.DevelopmentOnlyClaim, "true", ClaimValueTypes.Boolean));
        }

        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddMetrics().AddSingleton<SecurityMetrics>().BuildServiceProvider() };
        var context = new TokenValidatedContext(http, new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };

        await options.Events.OnTokenValidated(context);

        Assert.Equal(accepted, context.Result is null);
        Assert.Equal(!accepted, context.Result?.Failure is not null);
    }

    [Fact]
    public void Applying_the_rules_turns_every_check_on_and_hides_error_details()
    {
        var options = new JwtBearerOptions { IncludeErrorDetails = true };
        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.ValidateLifetime = false;
        options.TokenValidationParameters.RequireExpirationTime = false;
        options.TokenValidationParameters.RequireSignedTokens = false;

        TokenValidation.Apply(options, new StubHostEnvironment("Production"), new ConfigurationBuilder().Build());

        var parameters = options.TokenValidationParameters;
        Assert.True(parameters.ValidateIssuer && parameters.ValidateAudience && parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuerSigningKey && parameters.RequireExpirationTime && parameters.RequireSignedTokens);
        Assert.Equal(TimeSpan.FromMinutes(1), parameters.ClockSkew);
        Assert.False(options.IncludeErrorDetails);
        Assert.NotNull(options.Events?.OnAuthenticationFailed);
    }
}
