using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ContentItemDtoValidator : AbstractValidator<ContentItemDto>
{
    public const int MaxOptions = 20;

    public ContentItemDtoValidator()
    {
        RuleFor(item => item.Code).NotEmpty().MaximumLength(Codes.MaxContentCodeLength);
        RuleFor(item => item.DisplayOrder).GreaterThan(0);
        RuleFor(item => item.ItemType).IsInEnum();
        RuleFor(item => item.ScoringDirection).IsInEnum();
        RuleFor(item => item.ScoringCategory).MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(item => item.Prompt).NotNull().SetValidator(new TextDtoValidator());
        RuleFor(item => item.Options)
            .NotEmpty()
            .Must(options => options is null || options.Count <= MaxOptions)
            .WithMessage($"An item has at most {MaxOptions} options.");
        RuleForEach(item => item.Options).NotNull().SetValidator(new ContentOptionDtoValidator());
    }
}
