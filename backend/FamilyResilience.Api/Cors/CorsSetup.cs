using Microsoft.AspNetCore.Cors.Infrastructure;

namespace FamilyResilience.Api.Cors;

public static class CorsSetup
{
    public const string PolicyName = "ApiClients";

    /// <summary>Only origins listed in Cors:AllowedOrigins may call the API from a browser. None by default.</summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            options.AddPolicy(PolicyName, policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE"));
        });
        return services;
    }
}
