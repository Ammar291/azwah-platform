using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Assistant;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Application.SupportResources;
using FamilyResilience.Domain.Safeguarding;
using FamilyResilience.Domain.Scoring;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FamilyResilience.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(ApplicationAssembly.Reference, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<HouseholdAssignmentPlanner>();
        services.AddScoped<IHouseholdAssignmentService, HouseholdAssignmentService>();
        services.AddScoped<CallerAccess>();
        services.AddScoped<ICallerAccess>(provider => provider.GetRequiredService<CallerAccess>());
        services.TryAddScoped<IParticipantAccess>(provider => provider.GetRequiredService<CallerAccess>());
        services.AddSingleton<IAssessmentScoringStrategy, SummedItemValuesStrategy>();
        services.AddSingleton<ScoringStrategyCatalog>();
        services.AddSingleton<IRuleDefinitionValidator, EligibilityDefinitionValidator>();
        services.AddSingleton<IRuleDefinitionValidator, ScoringDefinitionValidator>();
        services.AddSingleton<IRuleDefinitionValidator, ReferralDefinitionValidator>();
        services.AddScoped<IdempotencyGuard>();
        services.AddScoped<SessionScoringService>();
        services.AddScoped<IMemberAssessmentCloser, MemberAssessmentCloser>();
        services.AddScoped<ScoreScaleReader>();
        services.AddScoped<DemoAnswerPlanner>();
        services.AddSingleton<DemoAnswerPlanCache>();
        services.AddScoped<ReferralRouter>();
        services.AddScoped<IOutboxMessageHandler, ExportRequestedHandler>();
        services.AddSingleton<SupportResourceMetrics>();
        services.AddScoped<DeterministicSupportResourceAssistant>();
        // The model path runs only when switched on; the API refuses to start that way without an approval reference and
        // a registered ILanguageModelClient, and none ships (LanguageModelStartupGuard).
        services.AddScoped<ISupportResourceAssistant>(provider => provider.GetRequiredService<LanguageModelSettings>().Enabled
            ? ActivatorUtilities.CreateInstance<LanguageModelSupportResourceAssistant>(provider)
            : provider.GetRequiredService<DeterministicSupportResourceAssistant>());
        // Without an adapter the assistant and the consultant answer "unavailable"; Infrastructure registers the OpenAI ones
        // over these defaults (D-119, D-124), and the safety check over its no-op default (D-126).
        services.TryAddSingleton<IAssistantModel, UnconfiguredAssistantModel>();
        services.TryAddSingleton<IVoiceConsultantModel, UnconfiguredVoiceConsultantModel>();
        services.TryAddSingleton<IAssistantSafetyCheck, NoAssistantSafetyCheck>();
        // A call stopped by the provider's filter carries on with a supportive reply (D-141): without a writer the fixed
        // words are said; Infrastructure registers the OpenAI one over this default.
        services.TryAddSingleton<IConsultantCrisisReply, NoConsultantCrisisReply>();
        services.AddSingleton<ConsultantHistoryScreen>();
        services.TryAddSingleton(AssistantLimits.Default);
        services.AddSingleton<AssistantMetrics>();
        services.AddSingleton<LiveConsultantSessions>();
        services.AddScoped<FamilyContextBuilder>();
        // The agent's allow-listed tools (D-122), scoped like the use cases they call.
        services.AddScoped<IAssistantTool, SearchSupportDirectoryTool>();
        services.AddScoped<IAssistantTool, SuggestAppScreenTool>();
        services.AddScoped<AssistantToolbox>();
        services.AddScoped<IAssistantToolRunner>(provider => provider.GetRequiredService<AssistantToolbox>());
        services.AddScoped<AssistantAgent>();
        // The phrase list is loaded and validated here, so an invalid list fails start-up, not the first message (D-136).
        services.AddSingleton(new CrisisSignalScreen(CrisisSignalCatalog.Load()));
        services.AddSingleton<SafeguardingMetrics>();
        services.AddScoped<ISafetySignalRecorder, RecordSafetySignal>();
        services.AddScoped<IOutboxMessageHandler, ScoreResultRecordedSafetyHandler>();
        // The written score report (D-112, D-113) and the support knowledge graph's authorities (D-114).
        services.AddScoped<ListSupportAuthorities>();
        services.AddScoped<ScoreReportPreparation>();
        services.AddScoped<ScoreReportModelCalls>();
        AddUseCases(services);
        return services;
    }

    /// <summary>Every public class with a public <c>HandleAsync</c> method is a use case, created per request.</summary>
    private static void AddUseCases(IServiceCollection services)
    {
        foreach (var useCase in ApplicationAssembly.Reference.GetTypes()
                     .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && type.GetMethod("HandleAsync") is not null))
        {
            services.AddScoped(useCase);
        }
    }
}
