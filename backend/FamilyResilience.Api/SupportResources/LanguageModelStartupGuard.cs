using FamilyResilience.Application.SupportResources;

namespace FamilyResilience.Api.SupportResources;

/// <summary>
/// Refuses to start an API (in any environment) whose support-resource language model is switched on but cannot run:
/// no approval reference naming the approving decision, no registered <see cref="ILanguageModelClient"/> (none ships,
/// and no provider is approved), or a setting out of range. The error names problem codes only, never setting values.
/// </summary>
public static class LanguageModelStartupGuard
{
    public static void ThrowIfInvalid(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var settings = services.GetRequiredService<LanguageModelSettings>();
        var clientRegistered = services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ILanguageModelClient));

        var problems = settings.Problems(clientRegistered);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "SupportResources:LanguageModel is enabled but cannot run: " + string.Join(", ", problems));
        }
    }
}
