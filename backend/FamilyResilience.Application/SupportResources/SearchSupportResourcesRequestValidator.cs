using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

public sealed class SearchSupportResourcesRequestValidator : AbstractValidator<SearchSupportResourcesRequest>
{
    public const int MaxFilters = 5;
    public const int MaxLimit = 10;

    public SearchSupportResourcesRequestValidator()
    {
        RuleFor(request => request.Text).MaximumLength(SupportResourceQueryText.MaxTextLength);
        RuleFor(request => request.Text)
            .NotEmpty()
            .When(request => request.Categories is not { Count: > 0 });
        RuleFor(request => request.Categories)
            .Must(categories => categories!.Count <= MaxFilters)
            .When(request => request.Categories is not null);
        RuleForEach(request => request.Categories).Must(code => code is not null && Codes.IsSnakeCode(code));
        RuleFor(request => request.Audiences)
            .Must(audiences => audiences!.Count <= MaxFilters)
            .When(request => request.Audiences is not null);
        RuleForEach(request => request.Audiences).Must(code => code is not null && Codes.IsSnakeCode(code));
        RuleFor(request => request.Limit).InclusiveBetween(1, MaxLimit).When(request => request.Limit is not null);
    }
}
