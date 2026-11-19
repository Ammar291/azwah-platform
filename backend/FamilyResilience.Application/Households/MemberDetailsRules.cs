using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FluentValidation;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Shape rules for any member details. Whether the birth month is in the future, and the roster rules (one head, one
/// primary earner, both adults), are decided by the household aggregate.
/// </summary>
public sealed class MemberDetailsRules : AbstractValidator<IMemberDetailsRequest>
{
    public MemberDetailsRules()
    {
        RuleFor(request => request.BirthYear).InclusiveBetween(YearMonth.MinYear, 9999);
        RuleFor(request => request.BirthMonth).InclusiveBetween(1, 12);
        RuleFor(request => request.RelationshipToHousehold).IsInEnum();
        RuleFor(request => request.EmploymentStatus).IsInEnum();
        RuleFor(request => request.IsHeadOfHousehold).NotNull();
        RuleFor(request => request.IsPrimaryIncomeEarner).NotNull();
        RuleFor(request => request.RelationshipToHousehold)
            .Null()
            .When(request => request.IsHeadOfHousehold == true)
            .WithMessage("The head of household has no relationship to the head.");
    }

    /// <summary>The domain's details for a validated request, or the birth-month error.</summary>
    public static Result<FamilyMemberDetails> ToDetails(IMemberDetailsRequest request, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(request);

        var birth = YearMonth.Create(request.BirthYear, request.BirthMonth, today);
        return birth.IsFailure
            ? Result.Failure<FamilyMemberDetails>(birth.Error!)
            : Result.Success(new FamilyMemberDetails(
                birth.Value,
                request.RelationshipToHousehold,
                request.EmploymentStatus,
                request.IsHeadOfHousehold == true,
                request.IsPrimaryIncomeEarner == true));
    }
}
