using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ChangeInstrumentTextRequestValidator : AbstractValidator<ChangeInstrumentTextRequest>
{
    public ChangeInstrumentTextRequestValidator()
    {
        RuleFor(request => request.Name).NotNull().SetValidator(new TextDtoValidator());
        RuleFor(request => request.Description).NotNull().SetValidator(new TextDtoValidator());
    }
}
