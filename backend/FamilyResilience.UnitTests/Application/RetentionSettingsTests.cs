using FamilyResilience.Application.Households;

namespace FamilyResilience.UnitTests.Application;

public sealed class RetentionSettingsTests
{
    private static RetentionSettings From(Dictionary<string, string?> settings) =>
        RetentionSettings.From(key => settings.GetValueOrDefault(key));

    [Fact]
    public void Retention_is_off_and_has_no_periods_by_default() =>
        Assert.Equal(new RetentionSettings(false, null, null, null), From([]));

    [Theory]
    [InlineData("0")]
    [InlineData("-30")]
    [InlineData("thirty")]
    [InlineData("36501")]
    [InlineData("")]
    public void An_unusable_period_deletes_nothing(string configured) =>
        Assert.Null(From(new() { ["Retention:WithdrawnHouseholdDays"] = configured }).WithdrawnHouseholdDays);

    [Fact]
    public void Configured_periods_are_read()
    {
        var settings = From(new()
        {
            ["Retention:Enabled"] = "true",
            ["Retention:WithdrawnHouseholdDays"] = "365",
            ["Retention:ProcessedOutboxDays"] = "30",
            ["Retention:FinishedExportJobsDays"] = "90",
        });

        Assert.Equal(new RetentionSettings(true, 365, 30, 90), settings);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("delete", true)]
    [InlineData(" delete ", true)]
    [InlineData("Delete", false)]
    [InlineData("retain_evidence", false)]
    public void Only_delete_is_a_built_consent_record_rule(string? configured, bool deletes) =>
        Assert.Equal(deletes, ErasureSettings.From(configured).DeletesConsentRecords);
}
