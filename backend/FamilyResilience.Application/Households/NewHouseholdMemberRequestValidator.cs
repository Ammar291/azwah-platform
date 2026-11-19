using FluentValidation;

namespace FamilyResilience.Application.Households;

public sealed class NewHouseholdMemberRequestValidator : AbstractValidator<NewHouseholdMemberRequest>
{
    public NewHouseholdMemberRequestValidator()
    {
        Include(new MemberDetailsRules());
        RuleFor(request => request.IsSelf).NotNull();
    }
}
