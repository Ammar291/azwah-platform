using FluentValidation;

namespace FamilyResilience.Application.Safeguarding;

public sealed class ResolveSafetyAlertRequestValidator : AbstractValidator<ResolveSafetyAlertRequest>
{
    public ResolveSafetyAlertRequestValidator() => RuleFor(request => request.Resolution).NotNull().IsInEnum();
}
