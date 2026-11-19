using FamilyResilience.Domain.Households;
using FluentValidation;

namespace FamilyResilience.Application.Households;

public sealed class UpdateHouseholdRequestValidator : AbstractValidator<UpdateHouseholdRequest>
{
    public UpdateHouseholdRequestValidator() =>
        RuleFor(request => request.Status)
            .NotNull()
            .Equal(HouseholdStatus.Withdrawn)
            .WithMessage("The only change a household can make is \"status\": \"withdrawn\".");
}
