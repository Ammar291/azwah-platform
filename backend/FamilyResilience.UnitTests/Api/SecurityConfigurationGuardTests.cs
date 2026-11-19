using FamilyResilience.Api.Hosting;
using FamilyResilience.UnitTests.Support;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.UnitTests.Api;

public sealed class SecurityConfigurationGuardTests
{
    private const string Secret = "S3cret-Passw0rd";

    private static Dictionary<string, string?> Deployed() => new()
    {
        ["ConnectionStrings:Default"] = $"Host=db.internal;Database=azwah;Username=api;Password={Secret};SSL Mode=VerifyFull",
        ["Authentication:Schemes:Bearer:Authority"] = "https://issuer.example.org",
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "azwah-api",
        ["Cors:AllowedOrigins:0"] = "https://admin.example.org",
    };

    private static IReadOnlyList<string> ProblemsWith(Action<Dictionary<string, string?>> change)
    {
        var settings = Deployed();
        change(settings);
        return SecurityConfigurationGuard.Problems(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    [Fact]
    public void Valid_deployed_settings_have_no_problems() => Assert.Empty(ProblemsWith(_ => { }));

    [Fact]
    public void A_single_audience_setting_is_enough() =>
        Assert.Empty(ProblemsWith(settings =>
        {
            settings.Remove("Authentication:Schemes:Bearer:ValidAudiences:0");
            settings["Authentication:Schemes:Bearer:ValidAudience"] = "azwah-api";
        }));

    [Fact]
    public void An_assistant_and_report_model_key_is_accepted_with_an_approval_reference() =>
        Assert.Empty(ProblemsWith(settings =>
        {
            settings["Assistant:Model:ApiKey"] = "sk-synthetic";
            settings["Assistant:Model:ApprovalReference"] = "DPA-2026-01";
        }));

    public static TheoryData<string, string, string?> Problems => new()
    {
        { "assistant_model_not_approved", "Assistant:Model:ApiKey", "sk-synthetic" },
        { "authority_missing", "Authentication:Schemes:Bearer:Authority", null },
        { "authority_not_https", "Authentication:Schemes:Bearer:Authority", "http://issuer.example.org" },
        { "audience_missing", "Authentication:Schemes:Bearer:ValidAudiences:0", null },
        { "symmetric_signing_keys", "Authentication:Schemes:Bearer:SigningKeys:0:Value", "c2VjcmV0" },
        { "metadata_not_https", "Authentication:Schemes:Bearer:RequireHttpsMetadata", "false" },
        { "metadata_not_https", "Authentication:Schemes:Bearer:MetadataAddress", "http://issuer.example.org/.well-known/openid-configuration" },
        { "cors_origin_wildcard", "Cors:AllowedOrigins:1", "*" },
        { "cors_origin_not_https", "Cors:AllowedOrigins:1", "http://admin.example.org" },
        { "database_tls_not_required", "ConnectionStrings:Default", "Host=db.internal;Database=azwah;Username=api;Password=x" },
        { "database_tls_not_required", "ConnectionStrings:Default", "Host=db.internal;Database=azwah;Username=api;Password=x;SSL Mode=Prefer" },
        { "database_tls_not_required", "ConnectionStrings:Migrations", "Host=db.internal;Database=azwah;Username=owner;Password=x" },
        { "forwarded_headers_trust_all", "FORWARDEDHEADERS_ENABLED", "true" },
        { "forwarded_headers_trust_all", "ReverseProxy:KnownNetworks:0", "0.0.0.0/0" },
        { "forwarded_headers_trust_all", "ReverseProxy:KnownNetworks:0", "::/0" },
        { "forwarded_headers_trust_all", "ReverseProxy:KnownNetworks:0", "10.0.0.0/7" },
        { "rate_limiting_disabled", "RateLimiting:Enabled", "false" },
        { "migrations_on_startup", "Database:ApplyMigrationsOnStartup", "true" },
        { "migrations_on_startup", "Database:ApplyMigrationsOnStartup", " True " },
        { "demonstration_database", "Database:UseDemonstrationDatabase", "true" },
        { "demonstration_database", "Database:UseDemonstrationDatabase", " TRUE " },
        { "demonstration_database", "ConnectionStrings:Default", "Host=db.internal;Database=azwah_demo;Username=api;Password=x;SSL Mode=VerifyFull" },
        { "demonstration_database", "ConnectionStrings:Default", "Host=db.internal;DB=AZWAH_DEMO;Username=api;Password=x;SSL Mode=VerifyFull" },
        { "demonstration_database", "ConnectionStrings:Migrations", "Host=db.internal;Database=azwah_demo;Username=owner;Password=x;SSL Mode=VerifyFull" },
        { "demo_tools_enabled", "DemoTools:Enabled", "true" },
        { "demo_tools_enabled", "DemoTools:Enabled", " TRUE " },
    };

    [Theory]
    [InlineData("demo")]
    [InlineData("demo_azwah")]
    [InlineData("azwah_demonstration")]
    [InlineData("azwah_demo_archive")]
    [InlineData("azwahdemo")]
    public void A_database_name_that_only_contains_demo_elsewhere_is_accepted(string database)
    {
        var connectionString = $"Host=db.internal;Database={database};Username=api;Password=x;SSL Mode=VerifyFull";

        Assert.Empty(ProblemsWith(settings =>
        {
            settings["ConnectionStrings:Default"] = connectionString;
            settings["ConnectionStrings:Migrations"] = connectionString;
        }));
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/12")]
    [InlineData("fd00:1234::/32")]
    public void A_private_proxy_range_is_accepted(string network) =>
        Assert.Empty(ProblemsWith(settings => settings["ReverseProxy:KnownNetworks:0"] = network));

    [Theory]
    [MemberData(nameof(Problems))]
    public void Each_unsafe_setting_is_named(string problem, string key, string? value)
    {
        var problems = ProblemsWith(settings => settings[key] = value);

        Assert.Equal([problem], problems);
    }

    [Theory]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("verify-full")]
    public void Database_tls_modes_that_require_encryption_pass(string mode) =>
        Assert.Empty(ProblemsWith(settings =>
            settings["ConnectionStrings:Default"] = $"Host=db.internal;Database=azwah;Username=api;Password=x;SSL Mode={mode}"));

    [Fact]
    public void A_deployed_host_with_problems_refuses_to_start_naming_codes_but_no_values()
    {
        var settings = Deployed();
        settings["ConnectionStrings:Default"] = $"Host=db.internal;Database=azwah;Username=api;Password={Secret}";
        settings["Authentication:Schemes:Bearer:Authority"] = "http://issuer.example.org";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            SecurityConfigurationGuard.ThrowIfInvalid(configuration, new StubHostEnvironment("Production")));

        Assert.Contains("authority_not_https", error.Message, StringComparison.Ordinal);
        Assert.Contains("database_tls_not_required", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("issuer.example.org", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_hosts_are_not_checked(string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        SecurityConfigurationGuard.ThrowIfInvalid(configuration, new StubHostEnvironment(environment));
    }
}
