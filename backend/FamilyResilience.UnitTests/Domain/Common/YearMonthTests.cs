using System.Globalization;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.UnitTests.Domain.Common;

public sealed class YearMonthTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void Valid_year_and_month_are_kept()
    {
        var result = YearMonth.Create(1984, 3, Today);

        Assert.True(result.IsSuccess);
        Assert.Equal(1984, result.Value.Year);
        Assert.Equal(3, result.Value.Month);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Month_outside_1_to_12_is_rejected(int month) =>
        Assert.Equal(CommonErrors.MonthOutOfRange, YearMonth.Create(1984, month, Today).Error);

    [Fact]
    public void Year_before_1900_is_rejected() =>
        Assert.Equal(CommonErrors.YearOutOfRange, YearMonth.Create(1899, 12, Today).Error);

    [Fact]
    public void Month_after_today_is_rejected() =>
        Assert.Equal(CommonErrors.YearMonthInFuture, YearMonth.Create(2026, 11, Today).Error);

    [Fact]
    public void Current_month_is_accepted() =>
        Assert.True(YearMonth.Create(2026, 10, Today).IsSuccess);

    [Theory]
    [InlineData(2008, 10, "2026-10-01", 18)]
    [InlineData(2008, 10, "2026-09-30", 17)]
    [InlineData(2008, 1, "2026-01-01", 18)]
    [InlineData(2008, 12, "2026-11-30", 17)]
    [InlineData(2026, 10, "2026-10-11", 0)]
    public void Age_counts_the_birthday_as_the_first_of_the_birth_month(int year, int month, string date, int expected) =>
        Assert.Equal(
            expected,
            YearMonth.Create(year, month, Today).Value.AgeOn(DateOnly.Parse(date, CultureInfo.InvariantCulture)));

    // Only the birth month is stored, so the birthday may be any day of it: 18 is certain only once the month ends.
    [Theory]
    [InlineData(2008, 11, "2026-11-02", false)] // born 20 Nov 2008 would still be 17
    [InlineData(2008, 11, "2026-11-30", false)]
    [InlineData(2008, 11, "2026-12-01", true)]
    [InlineData(2008, 10, "2026-10-31", false)]
    [InlineData(2008, 10, "2026-11-01", true)]
    [InlineData(2008, 12, "2026-12-01", false)]
    [InlineData(2008, 12, "2026-12-31", false)]
    [InlineData(2008, 12, "2027-01-01", true)]
    [InlineData(2008, 1, "2026-01-31", false)]
    [InlineData(2008, 1, "2026-02-01", true)]
    [InlineData(2009, 1, "2026-12-31", false)]
    [InlineData(2000, 6, "2026-11-02", true)]
    public void Certainly_at_least_18_only_from_the_first_day_after_the_birth_month(int year, int month, string date, bool expected) =>
        Assert.Equal(
            expected,
            YearMonth.Create(year, month, Today).Value.IsCertainlyAtLeast(18, DateOnly.Parse(date, CultureInfo.InvariantCulture)));

    [Fact]
    public void Certain_age_never_exceeds_the_age_by_the_first_of_month_convention()
    {
        var born = YearMonth.Create(2008, 11, Today).Value;
        for (var date = new DateOnly(2026, 1, 1); date < new DateOnly(2027, 3, 1); date = date.AddDays(1))
        {
            Assert.True(!born.IsCertainlyAtLeast(18, date) || born.AgeOn(date) >= 18);
        }
    }

    [Fact]
    public void A_negative_number_of_years_is_a_bug() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => YearMonth.Create(2008, 11, Today).Value.IsCertainlyAtLeast(-1, Today));
}
