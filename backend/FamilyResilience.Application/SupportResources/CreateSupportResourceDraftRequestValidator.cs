using FamilyResilience.Domain.Common;
using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

public sealed class CreateSupportResourceDraftRequestValidator : AbstractValidator<CreateSupportResourceDraftRequest>
{
    public CreateSupportResourceDraftRequestValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(Codes.MaxSnakeCodeLength);
        Include(new SupportResourceContentRules());
    }
}
