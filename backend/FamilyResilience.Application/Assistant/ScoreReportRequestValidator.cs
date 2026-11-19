using FluentValidation;

namespace FamilyResilience.Application.Assistant;

/// <summary>The language must be <c>en</c> or <c>ar</c>.</summary>
public sealed class ScoreReportRequestValidator : AbstractValidator<ScoreReportRequest>
{
    public ScoreReportRequestValidator() =>
        RuleFor(request => request.Language)
            .Must(AssistantLanguages.IsSupported)
            .WithMessage("Use en or ar.");
}
