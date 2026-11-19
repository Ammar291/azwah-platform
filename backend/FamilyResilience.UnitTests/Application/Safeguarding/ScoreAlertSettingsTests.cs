using FamilyResilience.Application.Safeguarding;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

public sealed class ScoreAlertSettingsTests
{
    [Fact]
    public void The_default_tiers_are_valid()
    {
        // The block in appsettings.json says the same thing (ScoreAlertSettingsReaderTests reads the file itself).
        var settings = new ScoreAlertSettings { Tiers = { ["household_assessment_total"] = ["vulnerable"] } };

        Assert.True(settings.Enabled);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void No_tiers_are_valid_and_raise_nothing()
    {
        var settings = new ScoreAlertSettings();

        Assert.Equal(
            ("Safeguarding:ScoreAlerts", "Safeguarding:ScoreAlerts:Enabled", "Safeguarding:ScoreAlerts:Tiers"),
            (ScoreAlertSettings.Section, ScoreAlertSettings.EnabledKey, ScoreAlertSettings.TiersSection));
        Assert.Empty(settings.Tiers);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void A_rule_set_may_list_several_tiers_or_none()
    {
        var settings = new ScoreAlertSettings
        {
            Tiers = { ["household_assessment_total"] = ["vulnerable", "transitional"], ["family_voices_total"] = [] },
        };

        Assert.Empty(settings.Problems());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData(" false ", false)]
    public void Enabled_is_on_unless_configured_false(string? configured, bool enabled)
    {
        var settings = new ScoreAlertSettings(configured);

        Assert.Equal(enabled, settings.Enabled);
        Assert.Empty(settings.Problems());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("off")]
    public void An_enabled_value_that_is_not_true_or_false_is_refused_and_never_reads_as_off(string configured)
    {
        var settings = new ScoreAlertSettings(configured);

        Assert.Equal(["enabled_invalid"], settings.Problems());
        Assert.True(settings.Enabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Household_Assessment_Total")]
    [InlineData("household-assessment-total")]
    [InlineData("household assessment total")]
    [InlineData("1_household")]
    public void A_rule_set_code_that_is_not_snake_case_is_refused(string ruleSet)
    {
        var settings = new ScoreAlertSettings { Tiers = { [ruleSet] = ["vulnerable"] } };

        Assert.Equal(["rule_set_code_invalid"], settings.Problems());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Vulnerable")]
    [InlineData("very vulnerable")]
    [InlineData("tier-1")]
    public void A_tier_that_is_not_snake_case_is_refused(string tier)
    {
        var settings = new ScoreAlertSettings { Tiers = { ["household_assessment_total"] = ["vulnerable", tier] } };

        Assert.Equal(["tier_code_invalid"], settings.Problems());
    }

    [Fact]
    public void Every_problem_is_named_and_no_value_is()
    {
        var settings = new ScoreAlertSettings("Secret Switch") { Tiers = { ["Secret Rule Set"] = ["Secret Tier"] } };

        var problems = settings.Problems();

        Assert.Equal(["enabled_invalid", "rule_set_code_invalid", "tier_code_invalid"], problems);
        Assert.DoesNotContain(problems, problem => problem.Contains("Secret", StringComparison.Ordinal));
    }
}
