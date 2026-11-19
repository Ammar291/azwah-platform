using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Privacy;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Infrastructure.Assistant;
using FamilyResilience.Infrastructure.Content;
using FamilyResilience.Infrastructure.Exports;
using FamilyResilience.Infrastructure.Persistence;
using FamilyResilience.Infrastructure.Persistence.Outbox;
using FamilyResilience.Infrastructure.Persistence.ReadModels;
using FamilyResilience.Infrastructure.Persistence.Repositories;
using FamilyResilience.Infrastructure.Safeguarding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FamilyResilience.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>The key of the one HttpClient the OpenAI adapters share.</summary>
    public const string OpenAiHttpClientKey = "openai";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<FamilyResilienceDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>()
                .GetConnectionString(FamilyResilienceDbContextOptions.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");
            }

            FamilyResilienceDbContextOptions.Configure(options, connectionString);
        });
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IWaveRepository, WaveRepository>();
        services.AddScoped<IAssessmentAssignmentRepository, AssessmentAssignmentRepository>();
        services.AddScoped<IAssessmentSessionRepository, AssessmentSessionRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IConsentPolicyVersionRepository, ConsentPolicyVersionRepository>();
        services.AddScoped<IConsentRecordRepository, ConsentRecordRepository>();
        services.AddScoped<IHouseholdRepository, HouseholdRepository>();
        services.AddScoped<IPlatformUserRepository, PlatformUserRepository>();
        services.AddScoped<IRoleSnapshotRepository, RoleSnapshotRepository>();
        services.AddScoped<IPrivacyRequestRepository, PrivacyRequestRepository>();
        services.AddScoped<IPrivacyRequestReadModel, PrivacyRequestReadModel>();
        services.AddScoped<ISafetyAlertRepository, SafetyAlertRepository>();
        services.AddScoped<ISafetyAlertReadModel, SafetyAlertReadModel>();
        // Read when first resolved, so a test host's settings are visible; ScoreAlertStartupGuard refuses unreadable ones.
        services.AddSingleton(provider => ScoreAlertSettingsReader.Read(provider.GetRequiredService<IConfiguration>()));
        services.AddScoped<IScoreResultFacts, ScoreResultFactsReader>();
        services.AddScoped<IHouseholdErasure, Persistence.Erasure.HouseholdErasure>();
        services.AddSingleton(provider => ErasureSettings.From(provider.GetRequiredService<IConfiguration>()[ErasureSettings.Setting]));
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return RetentionSettings.From(key => configuration[key]);
        });
        services.AddScoped<IOutboxMaintenance, OutboxMaintenance>();
        services.AddScoped<IAccessQueries, AccessQueries>();
        services.AddScoped<IAssignmentReadModel, AssignmentReadModel>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ISessionLock, SessionLock>();
        services.AddScoped<ISessionReadModel, SessionReadModel>();
        services.AddScoped<IMonitoringReadModel, MonitoringReadModel>();
        services.AddScoped<IHouseholdResultReadModel, HouseholdResultReadModel>();
        services.AddScoped<IResilienceTrendReadModel, ResilienceTrendReadModel>();
        services.AddScoped<IAnalyticsQueries, AnalyticsQueries>();
        services.AddScoped<OutboxProcessor>();
        services.AddSingleton(provider => new OutboxOptions(
            int.TryParse(
                provider.GetRequiredService<IConfiguration>()["Outbox:MaxAttempts"],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var attempts) && attempts >= 1
                ? attempts
                : OutboxOptions.DefaultMaxAttempts));
        services.AddSingleton(provider => new AnalyticsSettings(
            int.TryParse(
                provider.GetRequiredService<IConfiguration>()["Analytics:MinimumGroupSize"],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var minimum) && minimum >= 1
                ? minimum
                : AnalyticsSettings.DefaultMinimumGroupSize));
        services.AddScoped<ISupportRecommendationRepository, SupportRecommendationRepository>();
        services.AddScoped<IHouseholdRunLock, HouseholdRunLock>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IInstrumentVersionRepository, InstrumentVersionRepository>();
        services.AddScoped<IRuleSetRepository, RuleSetRepository>();
        services.AddScoped<IScoreResultRepository, ScoreResultRepository>();
        services.AddScoped<ISupportReferralRepository, SupportReferralRepository>();
        services.AddScoped<IPartnerOrganisationRepository, PartnerOrganisationRepository>();
        services.AddScoped<ISupportResourceRepository, SupportResourceRepository>();
        services.AddScoped<ISupportResourceSearch, SupportResourceSearch>();
        // Read when first resolved, so a test host's settings are visible. No ILanguageModelClient is registered here.
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return LanguageModelSettings.From(key => configuration[key]);
        });
        // Registered over the Application's unconfigured defaults: without Assistant:Model:ApiKey they say they are not
        // configured and are never called (D-112, D-119, D-124, D-126). Settings are read when first resolved, so a test
        // host's settings are visible. One settings record and one HttpClient (racing connections, D-121) serve the chat
        // model, the safety check and the score report's model.
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return OpenAiSettings.From(key => configuration[key]);
        });
        services.AddKeyedSingleton(OpenAiHttpClientKey, (_, _) => new HttpClient(HappyEyeballs.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IRealtimeConnector, WebSocketRealtimeConnector>();
        services.AddSingleton<IAssistantModel>(provider => new OpenAiAssistantModel(
            provider.GetRequiredService<OpenAiSettings>(),
            provider.GetRequiredKeyedService<HttpClient>(OpenAiHttpClientKey),
            provider.GetRequiredService<ILogger<OpenAiAssistantModel>>()));
        services.AddSingleton<IAssistantSafetyCheck>(provider => new OpenAiModerationCheck(
            provider.GetRequiredService<OpenAiSettings>(),
            provider.GetRequiredKeyedService<HttpClient>(OpenAiHttpClientKey),
            provider.GetRequiredService<ILogger<OpenAiModerationCheck>>()));
        // The consultant's supportive reply after OpenAI's filter stopped a call (D-141): same key, its own model.
        services.AddSingleton<IConsultantCrisisReply>(provider => new OpenAiCrisisReply(
            provider.GetRequiredService<OpenAiSettings>(),
            provider.GetRequiredKeyedService<HttpClient>(OpenAiHttpClientKey),
            provider.GetRequiredService<ILogger<OpenAiCrisisReply>>()));
        services.AddSingleton<IVoiceConsultantModel>(provider => new OpenAiLiveConsultantModel(
            provider.GetRequiredService<OpenAiSettings>(),
            provider.GetRequiredService<IRealtimeConnector>(),
            provider.GetRequiredService<IAssistantSafetyCheck>(),
            provider.GetRequiredService<AssistantMetrics>(),
            provider.GetRequiredService<ILogger<OpenAiLiveConsultantModel>>()));
        services.AddSingleton<IScoreReportModel>(provider => new OpenAiScoreReportModel(
            provider.GetRequiredService<OpenAiSettings>(),
            provider.GetRequiredKeyedService<HttpClient>(OpenAiHttpClientKey),
            provider.GetRequiredService<ILogger<OpenAiScoreReportModel>>()));
        services.AddSingleton<ISupportKnowledgeGraph>(_ => SupportKnowledgeGraph.Parse(ContentFiles.Read(ContentFiles.SupportKnowledgeGraph)));
        // Demonstration tools (D-130): read when first resolved; a deployed API refuses to start with them on.
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return DemoToolsSettings.From(key => configuration[key]);
        });
        services.AddScoped<IReferralCoordinationReadModel, ReferralCoordinationReadModel>();
        services.AddScoped<IExportJobRepository, ExportJobRepository>();
        services.AddScoped<IExportQueries, ExportQueries>();

        // The export file store depends on the hosting environment, so the API registers the real one
        // (ExportFileStores.Create); any other host gets an unconfigured store rather than a resolution failure.
        // Settings are read when first resolved, not at registration, so a test host's settings are visible.
        services.TryAddSingleton<IExportFileStore, UnconfiguredExportFileStore>();
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return ExportSettings.Bounded(
                Positive(configuration["Exports:MaxRows"], ExportSettings.DefaultMaxRows),
                Positive(configuration["Exports:RetentionDays"], ExportSettings.DefaultRetentionDays),
                Positive(configuration["Exports:StalledAfterHours"], ExportSettings.DefaultStalledAfterHours));
        });
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return ExportQuotas.Bounded(
                Positive(configuration["Exports:MaxActivePerRequester"], ExportQuotas.DefaultMaxActivePerRequester),
                Positive(configuration["Exports:MaxPerRequesterPerDay"], ExportQuotas.DefaultMaxPerRequesterPerDay));
        });
        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessCheck>("database", tags: [HealthCheckTags.Ready]);
        return services;
    }

    private static int Positive(string? value, int fallback) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
        && parsed >= 1
            ? parsed
            : fallback;
}
