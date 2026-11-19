using FamilyResilience.Application.Abstractions;
using FluentValidation;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>The read model trusts its query, so the page bounds and both filters are checked here.</summary>
public sealed class SafetyAlertsQueryValidator : AbstractValidator<SafetyAlertsQuery>
{
    public SafetyAlertsQueryValidator()
    {
        Include(new PageRules());
        RuleFor(query => query.Status).IsInEnum();
        RuleFor(query => query.Category).IsInEnum();
    }
}
