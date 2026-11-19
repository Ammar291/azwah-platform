using FamilyResilience.Application.Abstractions;
using FluentValidation;

namespace FamilyResilience.Application.Assignments;

public sealed class MyAssignmentsRequestValidator : AbstractValidator<MyAssignmentsRequest>
{
    public MyAssignmentsRequestValidator()
    {
        Include(new PageRules());
        RuleFor(request => request.Status).IsInEnum();
    }
}
