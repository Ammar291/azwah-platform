using FluentValidation;

namespace FamilyResilience.Application.Consent;

public sealed class RecordConsentRequestValidator : AbstractValidator<RecordConsentRequest>
{
    public RecordConsentRequestValidator()
    {
        RuleFor(request => request.MemberId).NotEmpty();
        RuleFor(request => request.PolicyVersionId).NotEmpty();
        RuleFor(request => request.Decision).NotNull().IsInEnum();
    }
}
