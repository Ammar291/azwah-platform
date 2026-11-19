using FluentValidation;

namespace FamilyResilience.Application.Analytics;

public sealed class ScoreDistributionsRequestValidator : AbstractValidator<ScoreDistributionsRequest>
{
    public ScoreDistributionsRequestValidator() =>
        RuleFor(request => request.WaveId).NotEqual(Guid.Empty).When(request => request.WaveId is not null);
}
