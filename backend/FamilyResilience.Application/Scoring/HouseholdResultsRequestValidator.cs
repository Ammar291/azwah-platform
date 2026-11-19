using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FluentValidation;

namespace FamilyResilience.Application.Scoring;

public sealed class HouseholdResultsRequestValidator : AbstractValidator<HouseholdResultsRequest>
{
    public HouseholdResultsRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.WaveId).NotEqual(Guid.Empty).When(request => request.WaveId is not null);
        RuleFor(request => request.InstrumentCode)
            .Must(Codes.IsSnakeCode)
            .WithMessage("An instrument code is lowercase snake case.")
            .When(request => request.InstrumentCode is not null);
        RuleFor(request => request.Reference)
            .Must(reference => HouseholdReference.Parse(reference).IsSuccess)
            .WithMessage("A household reference looks like HH-XXXX-XXXX.")
            .When(request => request.Reference is not null);
    }
}
