using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.Questionnaires;

public sealed class ApproveInstrumentVersionRequestValidator : AbstractValidator<ApproveInstrumentVersionRequest>
{
    public ApproveInstrumentVersionRequestValidator()
    {
        RuleFor(request => request.ApprovalSource).NotEmpty().MaximumLength(Approval.MaxSourceLength);
        RuleFor(request => request.EffectiveFrom).NotEmpty().Must(UtcTime.IsUtc).WithMessage("Times must be in UTC (Z).");
        RuleFor(request => request.EffectiveTo).Must(UtcTime.IsUtc).WithMessage("Times must be in UTC (Z).");
    }
}
