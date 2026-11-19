using FluentValidation;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Type <c>start</c>; 0 to 40 messages, each 1 to 4,000 characters after trimming and 20,000 in all (either role may be
/// last); a memory of at most 2,000 characters. Messages never repeat the text.
/// </summary>
public sealed class ConsultantStartMessageValidator : AbstractValidator<ConsultantStartMessage>
{
    public ConsultantStartMessageValidator()
    {
        RuleFor(start => start.Type).Equal(ConsultantStartMessage.TypeName);
        RuleFor(start => start.Memory)
            .Must(memory => memory is null || memory.Trim().Length <= AssistantMemory.MaxLength)
            .WithMessage($"The memory may hold at most {AssistantMemory.MaxLength} characters.");
        RuleFor(start => start.Messages!.Count)
            .LessThanOrEqualTo(AssistantReplyRequestValidator.MaxMessages)
            .OverridePropertyName("messages")
            .When(start => start.Messages is not null);
        RuleForEach(start => start.Messages)
            .NotNull()
            .SetValidator(new AssistantMessageRequestValidator());
        RuleFor(start => start.Messages)
            .Must(messages => AssistantReplyRequestValidator.TotalLength(messages!) <= AssistantReplyRequestValidator.MaxTotalLength)
            .WithMessage($"The messages may hold at most {AssistantReplyRequestValidator.MaxTotalLength} characters in all.")
            .When(start => start.Messages is { Count: > 0 });
    }
}
