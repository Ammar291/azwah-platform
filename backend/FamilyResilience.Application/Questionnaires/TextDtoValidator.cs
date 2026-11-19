using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class TextDtoValidator : AbstractValidator<TextDto>
{
    public const int MaxLength = 2000;

    public TextDtoValidator()
    {
        RuleFor(text => text.En).NotEmpty().MaximumLength(MaxLength);
        RuleFor(text => text.Ar).MaximumLength(MaxLength);
    }
}
