using FluentValidation;

namespace FamilyResilience.Application.Assistant;

public sealed class ConsultantStartRequestValidator : AbstractValidator<ConsultantStartRequest>
{
    public ConsultantStartRequestValidator() =>
        RuleFor(request => request.Language)
            .Must(AssistantLanguages.IsSupported)
            .WithMessage("Use en or ar.");
}
