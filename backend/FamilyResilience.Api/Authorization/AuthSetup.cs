using FamilyResilience.Api.Identity;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FamilyResilience.Api.Authorization;

public static class AuthSetup
{
    /// <summary>
    /// JWT bearer tokens from an OpenID Connect issuer. Authority and audiences bind from the configuration section
    /// "Authentication:Schemes:Bearer", which is also where `dotnet user-jwts` writes its development settings. The
    /// validation rules (<see cref="TokenValidation"/>) are applied after that binding, which replaces them otherwise.
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.MapInboundClaims = false);
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<IHostEnvironment, IConfiguration>((options, environment, configuration) =>
            {
                options.TokenValidationParameters.NameClaimType = ClaimNames.Subject;
                options.TokenValidationParameters.RoleClaimType = ClaimNames.Role;
                TokenValidation.Apply(options, environment, configuration);
            });
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<RoleObservationCache>();
        return services;
    }

    /// <summary>
    /// Every endpoint requires a signed-in user unless it is explicitly marked AllowAnonymous. Each named policy needs a
    /// token subject and its roles; the participant and export policies add resource requirements that are decided from
    /// the platform's own records, and a failed resource requirement answers 404 (<see cref="AuthorizationResultHandler"/>).
    /// </summary>
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Participant, policy => Signed(policy).RequireRole(Roles.Participant))
            .AddPolicy(Policies.HouseholdManager, policy => Signed(policy)
                .RequireRole(Roles.Participant)
                .AddRequirements(new HouseholdManagerRequirement()))
            .AddPolicy(Policies.AssignmentParticipant, policy => Signed(policy)
                .RequireRole(Roles.Participant)
                .AddRequirements(new AssignmentParticipantRequirement()))
            .AddPolicy(Policies.SessionParticipant, policy => Signed(policy)
                .RequireRole(Roles.Participant)
                .AddRequirements(new SessionParticipantRequirement()))
            .AddPolicy(Policies.Researcher, policy => Signed(policy).RequireRole(Roles.Researcher))
            .AddPolicy(Policies.ExportRequester, policy => Signed(policy)
                .RequireRole(Roles.Researcher)
                .AddRequirements(new ExportRequesterRequirement()))
            .AddPolicy(Policies.ProgramAdministrator, policy => Signed(policy).RequireRole(Roles.ProgramAdministrator))
            .AddPolicy(Policies.SystemAdministrator, policy => Signed(policy).RequireRole(Roles.SystemAdministrator))
            .AddPolicy(Policies.SupportCoordinator, policy => Signed(policy).RequireRole(Roles.SupportCoordinator))
            .AddPolicy(Policies.PartnerReader, policy => Signed(policy)
                .RequireRole(Roles.SystemAdministrator, Roles.SupportCoordinator))
            .AddPolicy(Policies.SupportResourceReader, policy => Signed(policy)
                .RequireRole(Roles.Participant, Roles.SupportCoordinator, Roles.SystemAdministrator))
            .AddPolicy(Policies.AnalyticsReader, policy => Signed(policy)
                .RequireRole(Roles.Researcher, Roles.ProgramAdministrator))
            .AddPolicy(Policies.WaveReader, policy => Signed(policy)
                .RequireRole(Roles.Researcher, Roles.ProgramAdministrator, Roles.SystemAdministrator))
            .AddPolicy(Policies.QuestionnaireReader, policy => Signed(policy).RequireRole(Roles.All));
        services.AddScoped<IAuthorizationHandler, HouseholdManagerHandler>();
        services.AddScoped<IAuthorizationHandler, AssignmentParticipantHandler>();
        services.AddScoped<IAuthorizationHandler, SessionParticipantHandler>();
        services.AddScoped<IAuthorizationHandler, ExportRequesterHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
        return services;
    }

    private static AuthorizationPolicyBuilder Signed(AuthorizationPolicyBuilder policy) =>
        policy.RequireAuthenticatedUser().RequireClaim(ClaimNames.Subject);
}
