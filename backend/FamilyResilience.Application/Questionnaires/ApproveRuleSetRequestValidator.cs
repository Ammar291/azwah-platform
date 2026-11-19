using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ApproveRuleSetRequestValidator : AbstractValidator<ApproveRuleSetRequest>
{
    public ApproveRuleSetRequestValidator() =>
        RuleFor(request => request.ApprovalSource).NotEmpty().MaximumLength(Approval.MaxSourceLength);
}
