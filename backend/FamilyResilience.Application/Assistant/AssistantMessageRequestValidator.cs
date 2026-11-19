using FluentValidation;

namespace FamilyResilience.Application.Assistant;

/// <summary>One message: a known role and channel, and 1 to 4,000 characters after trimming. Never repeats the text.</summary>
public sealed class AssistantMessageRequestValidator : AbstractValidator<AssistantMessageRequest>
{
    public AssistantMessageRequestValidator()
    {
        RuleFor(item => item.Role).IsInEnum();
        RuleFor(item => item.Channel).IsInEnum().When(item => item.Channel is not null);
        RuleFor(item => item.Text)
            .Must(text => text?.Trim().Length is >= 1 and <= AssistantReplyRequestValidator.MaxMessageLength)
            .WithMessage($"Each message needs 1 to {AssistantReplyRequestValidator.MaxMessageLength} characters.");
    }
}
