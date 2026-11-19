using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Infrastructure.Assistant;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.Api.Hosting;

/// <summary>
/// Refuses to start a deployed API (<see cref="HostEnvironmentExtensions.IsDeployed"/>) whose security settings would
/// weaken it: tokens from an unverified or plain-HTTP issuer, no audience, development signing keys, a wildcard or
/// plain-HTTP browser origin, a database connection that does not require TLS, forwarded headers trusted from anyone
/// (ASP.NET Core's <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>, or a <see cref="TransportSetup.KnownNetworksSetting"/>
/// range wider than /8 for IPv4 or /32 for IPv6; list the proxies instead), an OpenAI key with no approval reference,
/// rate limiting switched off, migrations run by the API at start-up, the local demonstration database (D-140: the
/// switch, or a connection string whose database name ends in <c>_demo</c>), or demonstration tools switched on (D-130:
/// their answers are made up). The error names problem codes only, never setting values, because they can hold
/// credentials.
/// </summary>
public static class SecurityConfigurationGuard
{
    private const string Bearer = "Authentication:Schemes:Bearer";

    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var problems = new List<string>();
        var bearer = configuration.GetSection(Bearer);

        var authority = bearer["Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            problems.Add("authority_missing");
        }
        else if (!IsHttps(authority))
        {
            problems.Add("authority_not_https");
        }

        var audiences = bearer.GetSection("ValidAudiences").GetChildren().Select(child => child.Value).Append(bearer["ValidAudience"]);
        if (!audiences.Any(audience => !string.IsNullOrWhiteSpace(audience)))
        {
            problems.Add("audience_missing");
        }

        if (bearer.GetSection("SigningKeys").GetChildren().Any())
        {
            problems.Add("symmetric_signing_keys");
        }

        var metadataAddress = bearer["MetadataAddress"];
        if (string.Equals(bearer["RequireHttpsMetadata"], "false", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(metadataAddress) && !IsHttps(metadataAddress)))
        {
            problems.Add("metadata_not_https");
        }

        var origins = configuration.GetSection("Cors:AllowedOrigins").GetChildren().Select(child => child.Value ?? string.Empty).ToList();
        if (origins.Any(origin => origin.Contains('*', StringComparison.Ordinal)))
        {
            problems.Add("cors_origin_wildcard");
        }
        else if (origins.Any(origin => !IsHttps(origin)))
        {
            problems.Add("cors_origin_not_https");
        }

        // The application's connection and, when set, the schema owner's (D-86).
        var connectionStrings = new[] { FamilyResilienceDbContextOptions.ConnectionStringName, DatabaseMigrator.MigrationsConnectionStringName }
            .Select(configuration.GetConnectionString)
            .OfType<string>()
            .Where(connectionString => !string.IsNullOrWhiteSpace(connectionString))
            .ToList();
        if (connectionStrings.Any(connectionString => !FamilyResilienceDbContextOptions.RequiresTls(connectionString)))
        {
            problems.Add("database_tls_not_required");
        }

        var networks = configuration.GetSection(TransportSetup.KnownNetworksSetting).GetChildren().Select(child => child.Value ?? string.Empty);
        if (string.Equals(configuration["FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase)
            || networks.Any(TooWide))
        {
            problems.Add("forwarded_headers_trust_all");
        }

        // A key sends family context, conversations and a session's answers to OpenAI (the assistant, the consultant and
        // the score report share it): a deployed API needs the decision that approves it on record (D-112, D-119, open
        // questions 48 and 52).
        if (!string.IsNullOrWhiteSpace(configuration[OpenAiSettings.ApiKeySetting])
            && string.IsNullOrWhiteSpace(configuration[OpenAiSettings.ApprovalReferenceSetting]))
        {
            problems.Add("assistant_model_not_approved");
        }

        if (!RateLimitSettings.From(configuration).Enabled)
        {
            problems.Add("rate_limiting_disabled");
        }

        // Deployed databases are migrated by the schema owner (a migration bundle) before the API starts; the API's own
        // role cannot change the schema (D-86).
        // Read as Program reads it (GetValue<bool>, which trims), so no spelling of true slips past.
        if (bool.TryParse(configuration["Database:ApplyMigrationsOnStartup"]?.Trim(), out var migrates) && migrates)
        {
            problems.Add("migrations_on_startup");
        }

        // The local demonstration database holds approvals that no research owner gave (D-140): refused whether the
        // switch asks for it or a connection string names it directly.
        if (DemonstrationDatabase.IsRequested(configuration) || connectionStrings.Any(DemonstrationDatabase.NamesDemonstrationDatabase))
        {
            problems.Add("demonstration_database");
        }

        // Demonstration answers are made up and must never reach real data (D-130, rule 15).
        if (DemoToolsSettings.From(key => configuration[key]).Enabled)
        {
            problems.Add("demo_tools_enabled");
        }

        return problems;
    }

    public static void ThrowIfInvalid(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDeployed())
        {
            return;
        }

        var problems = Problems(configuration);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The API's security settings are not safe for a deployed environment: {string.Join(", ", problems)}. "
                + "See docs/security-model.md.");
        }
    }

    /// <summary>A trusted proxy range so wide that anyone could claim to be the proxy.</summary>
    private static bool TooWide(string network) =>
        System.Net.IPNetwork.TryParse(network.Trim(), out var range)
        && range.PrefixLength < (range.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 32 : 8);

    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
