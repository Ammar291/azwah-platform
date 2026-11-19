using FamilyResilience.Application.Safeguarding;

namespace FamilyResilience.Api.Safeguarding;

/// <summary>
/// Refuses to start an API (in any environment) whose <c>Safeguarding:ScoreAlerts</c> settings cannot be trusted (D-136):
/// an <c>Enabled</c> value that is not <c>true</c> or <c>false</c>, or a scoring rule-set code or tier under
/// <c>Tiers</c> that is not snake case, so a typo cannot quietly stop low-score alerts. The error names problem codes
/// only, never setting values.
/// </summary>
public static class ScoreAlertStartupGuard
{
    public static void ThrowIfInvalid(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var problems = services.GetRequiredService<ScoreAlertSettings>().Problems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"{ScoreAlertSettings.Section} is invalid: {string.Join(", ", problems)}");
        }
    }
}
