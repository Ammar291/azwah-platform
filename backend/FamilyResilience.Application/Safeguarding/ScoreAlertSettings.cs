using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// Which score tiers raise a <c>low_resilience_score</c> alert (D-136), from configuration section <see cref="Section"/>.
/// <see cref="Tiers"/> is keyed by the scoring rule set's code; <c>appsettings.json</c> holds the default,
/// <c>household_assessment_total</c> → <c>["vulnerable"]</c>, and an environment overrides a tier by its index
/// (<c>Safeguarding__ScoreAlerts__Tiers__household_assessment_total__0=&lt;tier&gt;</c>). <see cref="Enabled"/> is the
/// off switch: <c>Safeguarding:ScoreAlerts:Enabled=false</c> stops every alert from scores. The API refuses to start
/// while <see cref="Problems"/> names anything.
/// </summary>
public sealed class ScoreAlertSettings
{
    public const string Section = "Safeguarding:ScoreAlerts";
    public const string EnabledKey = $"{Section}:Enabled";
    public const string TiersSection = $"{Section}:Tiers";

    private readonly bool _enabledReadable;

    /// <summary>
    /// Settings from the configured <see cref="EnabledKey"/> value: absent (null) is on; otherwise it must read as
    /// <c>true</c> or <c>false</c> (any case, surrounding spaces ignored), or <see cref="Problems"/> names
    /// <c>enabled_invalid</c>.
    /// </summary>
    public ScoreAlertSettings(string? enabled = null)
    {
        var parsed = true;
        _enabledReadable = enabled is null || bool.TryParse(enabled.Trim(), out parsed);

        // An unreadable value counts as on, never as a quiet off; the API refuses to start with it anyway.
        Enabled = !_enabledReadable || parsed;
    }

    /// <summary>On unless configured <c>false</c>. Off, the score handler completes every message and records nothing.</summary>
    public bool Enabled { get; }

    /// <summary>Tier codes keyed by the scoring rule set's code; both are snake case. A rule set with no codes raises nothing.</summary>
    public Dictionary<string, string[]> Tiers { get; } = new(StringComparer.Ordinal);

    /// <summary>Why the settings cannot be used, as fixed codes that never quote a configured value; empty when valid.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!_enabledReadable)
        {
            problems.Add("enabled_invalid");
        }

        if (Tiers.Keys.Any(ruleSetCode => !Codes.IsSnakeCode(ruleSetCode)))
        {
            problems.Add("rule_set_code_invalid");
        }

        if (Tiers.Values.Any(tiers => tiers is null || tiers.Any(tier => !Codes.IsSnakeCode(tier))))
        {
            problems.Add("tier_code_invalid");
        }

        return problems;
    }
}
