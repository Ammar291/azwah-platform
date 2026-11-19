using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Analytics;

public sealed class AssessmentMonitoringRequestValidator : AbstractValidator<AssessmentMonitoringRequest>
{
    public AssessmentMonitoringRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.WaveId).NotEqual(Guid.Empty).When(request => request.WaveId is not null);
        RuleFor(request => request.HouseholdId).NotEqual(Guid.Empty).When(request => request.HouseholdId is not null);
        RuleFor(request => request.InstrumentCode)
            .Must(Codes.IsSnakeCode)
            .WithMessage("An instrument code is lowercase snake case.")
            .When(request => request.InstrumentCode is not null);
        RuleFor(request => request.Status).IsInEnum();
        RuleFor(request => request.VersionNumber)
            .GreaterThan(0)
            .Must((request, _) => request.InstrumentCode is not null)
            .WithMessage("A version number needs an instrument code.")
            .When(request => request.VersionNumber is not null);
        RuleFor(request => request.AssignedFrom)
            .InclusiveBetween(AnalyticsOverviewRequest.EarliestDate, AnalyticsOverviewRequest.LatestDate)
            .When(request => request.AssignedFrom is not null);
        RuleFor(request => request.AssignedTo)
            .InclusiveBetween(AnalyticsOverviewRequest.EarliestDate, AnalyticsOverviewRequest.LatestDate)
            .When(request => request.AssignedTo is not null);
        RuleFor(request => request.AssignedFrom)
            .Must((request, from) => from <= request.AssignedTo)
            .WithMessage("The first assigned day must not be after the last.")
            .When(request => request.AssignedFrom is not null && request.AssignedTo is not null);
    }
}
