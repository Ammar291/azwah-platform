using FamilyResilience.Domain.Households;
using FluentValidation;

namespace FamilyResilience.Application.Households;

public sealed class CreateHouseholdRequestValidator : AbstractValidator<CreateHouseholdRequest>
{
    /// <summary>The domain's roster cap, so an oversized roster is a 400 before it reaches the household.</summary>
    public const int MaxMembers = Household.MaxActiveMembers;

    public CreateHouseholdRequestValidator()
    {
        RuleFor(request => request.Members).NotEmpty();
        RuleFor(request => request.Members!.Count).LessThanOrEqualTo(MaxMembers).OverridePropertyName("members")
            .When(request => request.Members is not null);
        RuleForEach(request => request.Members).SetValidator(new NewHouseholdMemberRequestValidator());
        RuleFor(request => request.Members)
            .Must(members => members!.Count(member => member.IsSelf == true) == 1)
            .When(request => request.Members is { Count: > 0 })
            .WithMessage("Exactly one member must be marked isSelf.");
    }
}
