using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class EffectiveToRequestValidator : AbstractValidator<EffectiveToRequest>
{
    public EffectiveToRequestValidator() =>
        RuleFor(request => request.EffectiveTo).NotEmpty().Must(UtcTime.IsUtc).WithMessage("Times must be in UTC (Z).");
}
