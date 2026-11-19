using FamilyResilience.Infrastructure.Safeguarding;
using FamilyResilience.UnitTests.Support;
using Microsoft.Extensions.Configuration;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class ScoreAlertSettingsReaderTests
{
    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>
    /// The API's own <c>appsettings.json</c>, with <paramref name="overrides"/> added last as an environment would add
    /// them (<c>Safeguarding__ScoreAlerts__Enabled</c> arrives as <c>Safeguarding:ScoreAlerts:Enabled</c>).
    /// </summary>
    private static IConfiguration Shipped(Dictionary<string, string?>? overrides = null) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryFiles.Root(), "backend", "FamilyResilience.Api", "appsettings.json"), optional: false)
            .AddInMemoryCollection(overrides ?? [])
            .Build();

    [Fact]
    public void The_shipped_default_alerts_on_the_household_assessment_vulnerable_tier()
    {
        var settings = ScoreAlertSettingsReader.Read(Shipped());

        Assert.True(settings.Enabled);
        Assert.Equal(["household_assessment_total"], settings.Tiers.Keys);
        Assert.Equal(["vulnerable"], settings.Tiers["household_assessment_total"]);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void Enabled_false_switches_the_alert_off()
    {
        var settings = ScoreAlertSettingsReader.Read(Shipped(new() { ["Safeguarding:ScoreAlerts:Enabled"] = "false" }));

        Assert.False(settings.Enabled);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void A_tier_is_overridden_by_its_index()
    {
        var replaced = ScoreAlertSettingsReader.Read(Shipped(new()
        {
            ["Safeguarding:ScoreAlerts:Tiers:household_assessment_total:0"] = "transitional",
        }));
        var added = ScoreAlertSettingsReader.Read(Shipped(new()
        {
            ["Safeguarding:ScoreAlerts:Tiers:household_assessment_total:1"] = "transitional",
        }));

        Assert.Equal(["transitional"], replaced.Tiers["household_assessment_total"]);
        Assert.Equal(["vulnerable", "transitional"], added.Tiers["household_assessment_total"]);
    }

    [Fact]
    public void An_unreadable_enabled_value_is_refused()
    {
        var settings = ScoreAlertSettingsReader.Read(Shipped(new() { ["Safeguarding:ScoreAlerts:Enabled"] = "nope" }));

        Assert.Equal(["enabled_invalid"], settings.Problems());
    }

    [Fact]
    public void Each_rule_set_lists_its_tiers()
    {
        var settings = ScoreAlertSettingsReader.Read(Configuration(new()
        {
            ["Safeguarding:ScoreAlerts:Tiers:household_assessment_total:0"] = "vulnerable",
            ["Safeguarding:ScoreAlerts:Tiers:household_assessment_total:1"] = "transitional",
            ["Safeguarding:ScoreAlerts:Tiers:family_voices_total:0"] = "low",
        }));

        Assert.Equal(2, settings.Tiers.Count);
        Assert.Equal(["vulnerable", "transitional"], settings.Tiers["household_assessment_total"]);
        Assert.Equal(["low"], settings.Tiers["family_voices_total"]);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void Without_the_section_the_alert_is_on_with_no_tiers()
    {
        var settings = ScoreAlertSettingsReader.Read(Configuration([]));

        Assert.True(settings.Enabled);
        Assert.Empty(settings.Tiers);
        Assert.Empty(settings.Problems());
    }

    [Fact]
    public void A_tier_written_as_an_object_is_refused()
    {
        var settings = ScoreAlertSettingsReader.Read(Configuration(new()
        {
            ["Safeguarding:ScoreAlerts:Tiers:household_assessment_total:0:code"] = "vulnerable",
        }));

        Assert.Equal(["tier_code_invalid"], settings.Problems());
    }
}
