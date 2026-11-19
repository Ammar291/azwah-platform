using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Households;
using FluentValidation;

namespace FamilyResilience.Application.Analytics;

public sealed class ParticipationRequestValidator : AbstractValidator<ParticipationRequest>
{
    public ParticipationRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.Status).IsInEnum();
        RuleFor(request => request.Reference)
            .Must(reference => HouseholdReference.Parse(reference).IsSuccess)
            .WithMessage("A household reference looks like HH-XXXX-XXXX.")
            .When(request => request.Reference is not null);
    }
}
