using FluentValidation;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Each date must lie between 2000 and 2100, and the window actually used (with its defaults) must run forwards and span
/// at most three years, whether one bound, both or neither was given.
/// </summary>
public sealed class AnalyticsOverviewRequestValidator : AbstractValidator<AnalyticsOverviewRequest>
{
    public AnalyticsOverviewRequestValidator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        RuleFor(request => request.WaveId).NotEqual(Guid.Empty).When(request => request.WaveId is not null);
        RuleFor(request => request.Interval).IsInEnum();
        RuleFor(request => request.From)
            .InclusiveBetween(AnalyticsOverviewRequest.EarliestDate, AnalyticsOverviewRequest.LatestDate)
            .When(request => request.From is not null);
        RuleFor(request => request.To)
            .InclusiveBetween(AnalyticsOverviewRequest.EarliestDate, AnalyticsOverviewRequest.LatestDate)
            .When(request => request.To is not null);
        RuleFor(request => request.To)
            .Must(_ => false)
            .WithMessage("'to' must not be before 'from'.")
            .When(request => InBounds(request) && Span(request, timeProvider) < 0);
        RuleFor(request => request.To)
            .Must(_ => false)
            .WithMessage("The trend window is at most three years.")
            .When(request => InBounds(request) && Span(request, timeProvider) > AnalyticsOverviewRequest.MaxRangeDays);
    }

    private static bool InBounds(AnalyticsOverviewRequest request) =>
        request.From is not { } from || (from >= AnalyticsOverviewRequest.EarliestDate && from <= AnalyticsOverviewRequest.LatestDate)
            ? request.To is not { } to || (to >= AnalyticsOverviewRequest.EarliestDate && to <= AnalyticsOverviewRequest.LatestDate)
            : false;

    private static int Span(AnalyticsOverviewRequest request, TimeProvider timeProvider)
    {
        var (from, to) = request.Window(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        return to.DayNumber - from.DayNumber;
    }
}
