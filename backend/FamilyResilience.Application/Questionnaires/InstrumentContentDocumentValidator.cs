using FamilyResilience.Domain.Questionnaires;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class InstrumentContentDocumentValidator : AbstractValidator<InstrumentContentDocument>
{
    public const int MaxSections = 50;

    public InstrumentContentDocumentValidator()
    {
        RuleFor(document => document.ContentReferenceAr).NotEmpty().MaximumLength(InstrumentVersion.MaxContentReferenceLength);
        RuleFor(document => document.ContentReferenceEn).NotEmpty().MaximumLength(InstrumentVersion.MaxContentReferenceLength);
        RuleFor(document => document.Sections)
            .NotEmpty()
            .Must(sections => sections is null || sections.Count <= MaxSections)
            .WithMessage($"A version has at most {MaxSections} sections.");
        RuleForEach(document => document.Sections).NotNull().SetValidator(new ContentSectionDtoValidator());
    }
}
