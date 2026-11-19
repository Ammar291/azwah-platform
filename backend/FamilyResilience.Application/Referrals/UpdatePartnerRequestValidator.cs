using FluentValidation;

namespace FamilyResilience.Application.Referrals;

public sealed class UpdatePartnerRequestValidator : AbstractValidator<UpdatePartnerRequest>
{
    public UpdatePartnerRequestValidator() => RuleFor(request => request.IsActive).NotNull();
}
