using FamilyResilience.Application.Analytics;

namespace FamilyResilience.UnitTests.Application;

public sealed class ExportSettingsTests
{
    [Fact]
    public void Settings_are_kept_within_their_bounds()
    {
        var settings = ExportSettings.Bounded(int.MaxValue, int.MaxValue, int.MaxValue);

        Assert.Equal(
            (ExportSettings.MaxMaxRows, ExportSettings.MaxRetentionDays, ExportSettings.MaxStalledAfterHours),
            (settings.MaxRows, settings.RetentionDays, settings.StalledAfterHours));
    }

    [Fact]
    public void Values_below_one_become_one() =>
        Assert.Equal(new ExportSettings(1, 1, 1), ExportSettings.Bounded(0, -5, 0));

    [Fact]
    public void The_defaults_are_inside_the_bounds() =>
        Assert.Equal(ExportSettings.Default, ExportSettings.Bounded(ExportSettings.DefaultMaxRows, ExportSettings.DefaultRetentionDays, ExportSettings.DefaultStalledAfterHours));
}
