using FluentValidation;

namespace FamilyResilience.Application.Referrals;

public sealed class HandOffRequestValidator : AbstractValidator<HandOffRequest>
{
    public HandOffRequestValidator() => RuleFor(request => request.PartnerId).NotNull().NotEqual(Guid.Empty);
}
