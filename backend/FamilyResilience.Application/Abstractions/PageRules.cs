using FluentValidation;

namespace FamilyResilience.Application.Abstractions;

/// <summary>The paging bounds every collection shares; include it in the validator of any <see cref="IPageRequest"/>.</summary>
public sealed class PageRules : AbstractValidator<IPageRequest>
{
    public PageRules()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1);
        RuleFor(request => request.Page).LessThanOrEqualTo(int.MaxValue / PageRequestValidator.MaxPageSize);
        RuleFor(request => request.PageSize).InclusiveBetween(1, PageRequestValidator.MaxPageSize);
    }
}
