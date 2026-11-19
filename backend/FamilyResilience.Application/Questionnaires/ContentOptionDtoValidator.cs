using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ContentOptionDtoValidator : AbstractValidator<ContentOptionDto>
{
    public ContentOptionDtoValidator()
    {
        RuleFor(option => option.Code).NotEmpty().MaximumLength(Codes.MaxContentCodeLength);
        RuleFor(option => option.DisplayOrder).GreaterThan(0);
        RuleFor(option => option.Label).NotNull().SetValidator(new TextDtoValidator());
    }
}
