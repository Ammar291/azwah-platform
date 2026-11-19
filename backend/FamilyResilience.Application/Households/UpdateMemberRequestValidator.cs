using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FluentValidation;

namespace FamilyResilience.Application.Households;

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(request => request)
            .Must(request => request.ChangesDetails || request.Status.IsSet)
            .OverridePropertyName("request")
            .WithMessage("The patch changes nothing.");
        RuleFor(request => request.BirthYear.Value)
            .InclusiveBetween(YearMonth.MinYear, 9999)
            .OverridePropertyName("birthYear")
            .When(request => request.BirthYear.IsSet);
        RuleFor(request => request.BirthMonth.Value)
            .InclusiveBetween(1, 12)
            .OverridePropertyName("birthMonth")
            .When(request => request.BirthMonth.IsSet);
        RuleFor(request => request.RelationshipToHousehold.Value)
            .IsInEnum()
            .OverridePropertyName("relationshipToHousehold")
            .When(request => request.RelationshipToHousehold.IsSet);
        RuleFor(request => request.EmploymentStatus.Value)
            .IsInEnum()
            .OverridePropertyName("employmentStatus")
            .When(request => request.EmploymentStatus.IsSet);
        RuleFor(request => request.Status.Value)
            .Equal(MemberStatus.Inactive)
            .OverridePropertyName("status")
            .WithMessage("A member can only be made \"inactive\".")
            .When(request => request.Status.IsSet);
        RuleFor(request => request.Status.Value)
            .Must(_ => false)
            .OverridePropertyName("status")
            .WithMessage("Removing a member cannot be combined with other changes.")
            .When(request => request.Status.IsSet && request.ChangesDetails);
    }
}
