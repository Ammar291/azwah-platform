using FluentValidation;

namespace FamilyResilience.Application.Identity;

public sealed class RegistrationRequestValidator : AbstractValidator<RegistrationRequest>
{
    public RegistrationRequestValidator() =>
        RuleFor(request => request.DetailsConfirmed)
            .Must(confirmed => confirmed == true)
            .WithMessage("Confirm your details (detailsConfirmed must be true).");
}
