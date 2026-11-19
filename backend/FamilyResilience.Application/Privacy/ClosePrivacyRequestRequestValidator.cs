using FluentValidation;

namespace FamilyResilience.Application.Privacy;

public sealed class ClosePrivacyRequestRequestValidator : AbstractValidator<ClosePrivacyRequestRequest>
{
    public ClosePrivacyRequestRequestValidator() => RuleFor(request => request.Resolution).NotNull().IsInEnum();
}
