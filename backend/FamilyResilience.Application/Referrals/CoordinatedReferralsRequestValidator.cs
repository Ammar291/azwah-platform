using FamilyResilience.Application.Abstractions;
using FluentValidation;

namespace FamilyResilience.Application.Referrals;

public sealed class CoordinatedReferralsRequestValidator : AbstractValidator<CoordinatedReferralsRequest>
{
    public CoordinatedReferralsRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.Status).IsInEnum();
    }
}
