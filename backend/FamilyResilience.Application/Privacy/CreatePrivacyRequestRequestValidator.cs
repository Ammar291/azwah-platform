using FluentValidation;

namespace FamilyResilience.Application.Privacy;

public sealed class CreatePrivacyRequestRequestValidator : AbstractValidator<CreatePrivacyRequestRequest>
{
    public CreatePrivacyRequestRequestValidator()
    {
        RuleFor(request => request.Type).NotNull().IsInEnum();
        RuleFor(request => request.MemberId).NotEqual(Guid.Empty).When(request => request.MemberId is not null);
    }
}
