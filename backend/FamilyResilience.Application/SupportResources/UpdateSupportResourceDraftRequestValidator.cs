using FluentValidation;

namespace FamilyResilience.Application.SupportResources;

public sealed class UpdateSupportResourceDraftRequestValidator : AbstractValidator<UpdateSupportResourceDraftRequest>
{
    public UpdateSupportResourceDraftRequestValidator() => Include(new SupportResourceContentRules());
}
