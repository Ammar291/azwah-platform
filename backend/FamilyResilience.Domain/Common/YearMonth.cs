namespace FamilyResilience.Domain.Common;

/// <summary>
/// A year and month. Used for birth dates so the platform never stores a full date of birth (data minimisation).
/// </summary>
public sealed record YearMonth
{
    public const int MinYear = 1900;

    // EF Core binds this constructor when it reads rows.
    private YearMonth(int year, int month)
    {
        Year = year;
        Month = month;
    }

    public int Year { get; private init; }

    public int Month { get; private init; }

    public static Result<YearMonth> Create(int year, int month, DateOnly today)
    {
        if (month is < 1 or > 12)
        {
            return Result.Failure<YearMonth>(CommonErrors.MonthOutOfRange);
        }

        if (year < MinYear)
        {
            return Result.Failure<YearMonth>(CommonErrors.YearOutOfRange);
        }

        if (year > today.Year || (year == today.Year && month > today.Month))
        {
            return Result.Failure<YearMonth>(CommonErrors.YearMonthInFuture);
        }

        return Result.Success(new YearMonth(year, month));
    }

    /// <summary>Completed years on <paramref name="date"/>. The birthday counts as the first day of the birth month.</summary>
    public int AgeOn(DateOnly date) => date.Year - Year - (date.Month < Month ? 1 : 0);

    /// <summary>
    /// Whether the person has completed <paramref name="years"/> years on <paramref name="date"/> whatever the day of
    /// the month they were born on: true only from the first day of the month after the birth month, that many years
    /// on. Use it where an age is a safeguard (adults only, D-58): <see cref="AgeOn"/> counts the birthday as the first
    /// of the birth month, so it can call a person a year older than they are for up to a month.
    /// </summary>
    public bool IsCertainlyAtLeast(int years, DateOnly date)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(years);
        return (date.Year * 12) + date.Month > ((Year + years) * 12) + Month;
    }
}
