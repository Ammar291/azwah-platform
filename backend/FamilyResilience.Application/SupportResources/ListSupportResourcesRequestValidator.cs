using FamilyResilience.Application.Abstractions;
using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

public sealed class ListSupportResourcesRequestValidator : AbstractValidator<ListSupportResourcesRequest>
{
    public ListSupportResourcesRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.Status).IsInEnum();
    }
}
