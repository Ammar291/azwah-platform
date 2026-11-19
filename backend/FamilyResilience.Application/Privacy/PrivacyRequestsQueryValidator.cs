using FamilyResilience.Application.Abstractions;
using FluentValidation;

namespace FamilyResilience.Application.Privacy;

public sealed class PrivacyRequestsQueryValidator : AbstractValidator<PrivacyRequestsQuery>
{
    public PrivacyRequestsQueryValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.Status).IsInEnum();
    }
}
