using FamilyResilience.Domain.SupportResources;
using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Shape only; formats (https address, codes, Arabic script) are checked by <see cref="SupportResourceContent"/>.</summary>
public sealed class SupportResourceContentRules : AbstractValidator<ISupportResourceContentRequest>
{
    public SupportResourceContentRules()
    {
        RuleFor(request => request.NameEn).NotEmpty().MaximumLength(SupportResourceContent.MaxNameLength);
        RuleFor(request => request.NameAr).MaximumLength(SupportResourceContent.MaxNameLength);
        RuleFor(request => request.SummaryEn).NotEmpty().MaximumLength(SupportResourceContent.MaxSummaryLength);
        RuleFor(request => request.SummaryAr).MaximumLength(SupportResourceContent.MaxSummaryLength);
        RuleFor(request => request.ProviderNameEn).NotEmpty().MaximumLength(SupportResourceContent.MaxNameLength);
        RuleFor(request => request.ProviderNameAr).MaximumLength(SupportResourceContent.MaxNameLength);
        RuleFor(request => request.Url).NotEmpty().MaximumLength(SupportResourceContent.MaxUrlLength);
        RuleFor(request => request.Categories).NotEmpty();
        RuleFor(request => request.Categories)
            .Must(categories => categories!.Count <= SupportResourceContent.MaxCategories)
            .When(request => request.Categories is not null);
        RuleFor(request => request.Audiences)
            .Must(audiences => audiences!.Count <= SupportResourceContent.MaxAudiences)
            .When(request => request.Audiences is not null);
        RuleFor(request => request.ServiceLanguages).NotEmpty();
        RuleFor(request => request.ServiceLanguages)
            .Must(languages => languages!.Count <= SupportResourceContent.MaxServiceLanguages)
            .When(request => request.ServiceLanguages is not null);
        RuleFor(request => request.Visibility).NotNull().IsInEnum();
    }
}
