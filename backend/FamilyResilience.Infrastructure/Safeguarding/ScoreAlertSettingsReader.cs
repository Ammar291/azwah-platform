using FamilyResilience.Application.Safeguarding;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.Infrastructure.Safeguarding;

/// <summary>
/// Reads <see cref="ScoreAlertSettings"/> from <see cref="ScoreAlertSettings.Section"/>: <c>Enabled</c> as configured, and
/// under <c>Tiers</c> each child is a scoring rule set's code whose indexed children are its tier codes, so a later source
/// (an environment variable <c>Safeguarding__ScoreAlerts__Tiers__household_assessment_total__0</c>) replaces a tier by its
/// index. A tier written as an object is kept as an empty code, so <see cref="ScoreAlertSettings.Problems"/> refuses it.
/// </summary>
public static class ScoreAlertSettingsReader
{
    public static ScoreAlertSettings Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new ScoreAlertSettings(configuration[ScoreAlertSettings.EnabledKey]);
        foreach (var ruleSet in configuration.GetSection(ScoreAlertSettings.TiersSection).GetChildren())
        {
            settings.Tiers[ruleSet.Key] = [.. ruleSet.GetChildren().Select(tier => tier.Value ?? string.Empty)];
        }

        return settings;
    }
}
