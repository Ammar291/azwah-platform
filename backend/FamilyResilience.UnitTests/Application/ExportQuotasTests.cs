using FamilyResilience.Application.Analytics;

namespace FamilyResilience.UnitTests.Application;

public sealed class ExportQuotasTests
{
    [Fact]
    public void The_defaults_are_two_active_and_twenty_a_day() =>
        Assert.Equal(new ExportQuotas(2, 20), ExportQuotas.Default);

    [Fact]
    public void Quotas_are_kept_within_their_bounds()
    {
        Assert.Equal(new ExportQuotas(10, 200), ExportQuotas.Bounded(int.MaxValue, int.MaxValue));
        Assert.Equal(new ExportQuotas(1, 1), ExportQuotas.Bounded(0, -4));
    }
}
