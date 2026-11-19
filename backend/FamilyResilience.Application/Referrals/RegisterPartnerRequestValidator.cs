using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Referrals;
using FluentValidation;

namespace FamilyResilience.Application.Referrals;

/// <summary>Shape only; code and category formats are checked by <see cref="PartnerOrganisation.Register"/>.</summary>
public sealed class RegisterPartnerRequestValidator : AbstractValidator<RegisterPartnerRequest>
{
    public RegisterPartnerRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        RuleFor(request => request.NameAr).NotEmpty().MaximumLength(PartnerOrganisation.MaxNameLength);
        RuleFor(request => request.NameEn).NotEmpty().MaximumLength(PartnerOrganisation.MaxNameLength);
        RuleFor(request => request.Categories).NotEmpty();
        RuleFor(request => request.Categories!.Count).LessThanOrEqualTo(PartnerOrganisation.MaxCategories).OverridePropertyName("categories")
            .When(request => request.Categories is not null);
        RuleForEach(request => request.Categories).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
    }
}
