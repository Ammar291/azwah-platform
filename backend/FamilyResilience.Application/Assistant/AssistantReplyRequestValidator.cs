using FluentValidation;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// 1 to 40 messages, each 1 to 4,000 characters after trimming and 20,000 in all, the last from the person, each
/// <c>chat</c> or <c>voice</c>; a memory of at most 2,000 characters; language <c>en</c> or <c>ar</c>. Messages never
/// repeat the text.
/// </summary>
public sealed class AssistantReplyRequestValidator : AbstractValidator<AssistantReplyRequest>
{
    public const int MaxMessages = 40;
    public const int MaxMessageLength = 4_000;
    public const int MaxTotalLength = 20_000;

    public AssistantReplyRequestValidator()
    {
        RuleFor(request => request.Language)
            .Must(AssistantLanguages.IsSupported)
            .WithMessage("Use en or ar.");
        RuleFor(request => request.Memory)
            .Must(memory => memory is null || memory.Trim().Length <= AssistantMemory.MaxLength)
            .WithMessage($"The memory may hold at most {AssistantMemory.MaxLength} characters.");
        RuleFor(request => request.Messages).NotEmpty();
        RuleFor(request => request.Messages!.Count)
            .LessThanOrEqualTo(MaxMessages)
            .OverridePropertyName("messages")
            .When(request => request.Messages is not null);
        RuleForEach(request => request.Messages)
            .NotNull()
            .SetValidator(new AssistantMessageRequestValidator());
        RuleFor(request => request.Messages)
            .Must(messages => TotalLength(messages!) <= MaxTotalLength)
            .WithMessage($"The messages may hold at most {MaxTotalLength} characters in all.")
            .When(request => request.Messages is { Count: > 0 });
        RuleFor(request => request.Messages)
            .Must(messages => messages![^1]?.Role == AssistantMessageRole.User)
            .WithMessage("The last message must be the person's.")
            .When(request => request.Messages is { Count: > 0 });
    }

    internal static int TotalLength(IEnumerable<AssistantMessageRequest?> messages) =>
        messages.Sum(message => message?.Text?.Trim().Length ?? 0);
}
