using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class CreateInstrumentRequestValidator : AbstractValidator<CreateInstrumentRequest>
{
    public CreateInstrumentRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(request => request.Kind).IsInEnum();
        RuleFor(request => request.Name).NotNull().SetValidator(new TextDtoValidator());
        RuleFor(request => request.Description).NotNull().SetValidator(new TextDtoValidator());
    }
}
