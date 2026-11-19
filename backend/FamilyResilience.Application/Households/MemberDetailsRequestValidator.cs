using FluentValidation;

namespace FamilyResilience.Application.Households;

public sealed class MemberDetailsRequestValidator : AbstractValidator<MemberDetailsRequest>
{
    public MemberDetailsRequestValidator() => Include(new MemberDetailsRules());
}
