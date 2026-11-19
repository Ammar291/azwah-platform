using FamilyResilience.Domain.Scoring;
using FluentValidation;

namespace FamilyResilience.Application.Sessions;

public sealed class DemoAnswersRequestValidator : AbstractValidator<DemoAnswersRequest>
{
    private static readonly Dictionary<string, DemoScoreLevel> Levels = new(StringComparer.Ordinal)
    {
        ["low"] = DemoScoreLevel.Low,
        ["medium"] = DemoScoreLevel.Medium,
        ["high"] = DemoScoreLevel.High,
    };

    public DemoAnswersRequestValidator()
    {
        RuleFor(request => request.Level)
            .NotEmpty()
            .Must(level => level is not null && Levels.ContainsKey(level))
            .WithMessage("Level must be \"low\", \"medium\" or \"high\".");
    }

    /// <summary>The level a validated request names.</summary>
    public static DemoScoreLevel LevelOf(DemoAnswersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Levels[request.Level ?? string.Empty];
    }
}
