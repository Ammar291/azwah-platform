using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ContentSectionDtoValidator : AbstractValidator<ContentSectionDto>
{
    public const int MaxItems = 200;

    public ContentSectionDtoValidator()
    {
        RuleFor(section => section.Code).NotEmpty().MaximumLength(Codes.MaxContentCodeLength);
        RuleFor(section => section.DisplayOrder).GreaterThan(0);
        RuleFor(section => section.Title).NotNull().SetValidator(new TextDtoValidator());
        RuleFor(section => section.Items)
            .NotEmpty()
            .Must(items => items is null || items.Count <= MaxItems)
            .WithMessage($"A section has at most {MaxItems} items.");
        RuleForEach(section => section.Items).NotNull().SetValidator(new ContentItemDtoValidator());
    }
}
