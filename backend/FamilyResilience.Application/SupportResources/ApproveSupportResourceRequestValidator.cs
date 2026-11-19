using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

/// <summary>Shape only; the review window is checked by <c>SupportResource.Approve</c>.</summary>
public sealed class ApproveSupportResourceRequestValidator : AbstractValidator<ApproveSupportResourceRequest>
{
    public ApproveSupportResourceRequestValidator()
    {
        RuleFor(request => request.ApprovalSource).NotEmpty().MaximumLength(Approval.MaxSourceLength);
        RuleFor(request => request.ReviewDueAt).NotNull();
    }
}
