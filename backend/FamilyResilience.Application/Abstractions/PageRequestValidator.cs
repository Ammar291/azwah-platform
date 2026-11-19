using FluentValidation;

namespace FamilyResilience.Application.Abstractions;

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public const int MaxPageSize = 100;

    public PageRequestValidator() => Include(new PageRules());
}
